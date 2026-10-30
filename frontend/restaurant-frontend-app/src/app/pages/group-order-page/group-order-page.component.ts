import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { catchError, defer, EMPTY, exhaustMap, finalize, forkJoin, interval, map, Observable, Subscription, switchMap, tap } from 'rxjs';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupReservationStatus, GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';

type LoadState = 'loading' | 'ready' | 'not-found' | 'error';

const SYNC_INTERVAL_MS = 15_000;

const STATUS_LABELS: Record<GroupReservationStatus, string> = {
  Open: 'Collecting pre-orders',
  Submitted: 'Sent to Restaurant',
  Closed: 'Closed',
};

@Component({
  selector: 'app-group-order-page',
  imports: [ParticipantCardComponent, ModalComponent, DatePipe, CurrencyPipe],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent {
  private readonly groupOrders = inject(GroupOrdersService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly reservationId = inject(ActivatedRoute).snapshot.paramMap.get('reservationId') ?? '';

  readonly loadState = signal<LoadState>('loading');
  readonly invite = signal<GuestInvite | null>(null);
  readonly participants = signal<Participant[]>([]);
  readonly ownerSessionId = signal<string | null>(null);
  readonly status = signal<GroupReservationStatus>('Open');
  readonly submitting = signal(false);
  readonly confirmOpen = signal(false);

  // фонова синхронізація: час останніх успішних даних, чи впала остання спроба, чи йде запит
  readonly lastUpdated = signal<Date | null>(null);
  readonly refreshFailed = signal(false);
  readonly syncing = signal(false);

  private syncTimer: Subscription | null = null;

  // у замовлення ресторану йдуть лише підтверджені кошики
  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));
  readonly grandTotal = computed(() => this.consolidated().total);

  readonly isOpen = computed(() => this.status() === 'Open');
  readonly statusLabel = computed(() => STATUS_LABELS[this.status()]);
  readonly isEmpty = computed(() => this.participants().every((person) => person.items.length === 0));
  readonly canSubmit = computed(
    () => this.isOpen() && this.consolidated().items.length > 0 && !this.submitting()
  );

  readonly guestsLabel = computed(
    () => `${this.participants().length} of ${this.invite()?.MaxGuestCount ?? 0} guests`
  );

  readonly pendingCount = computed(() => this.participants().filter((person) => !person.isSubmitted).length);
  readonly pendingMessage = computed(() => {
    const count = this.pendingCount();
    const who = count === 1 ? '1 participant hasn\'t' : `${count} participants haven't`;
    return `${who} submitted their cart yet — their dishes won't be included in the order`;
  });

  constructor() {
    this.load();
  }

  load(): void {
    // Try again не повинен запускати другий таймер
    this.stopSync();
    this.loadState.set('loading');

    this.groupOrders
      .getGroupInvite$(this.reservationId)
      .pipe(
        switchMap((invite) =>
          forkJoin({
            sessions: this.groupOrders.getSessions$(invite.GroupReservationId),
            ownerSessionId: this.groupOrders.getOwnerSessionId$(this.reservationId),
            groupStatus: this.groupOrders.getStatus$(invite.GroupReservationId),
          }).pipe(map((data) => ({ invite, ...data })))
        ),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: ({ invite, sessions, ownerSessionId, groupStatus }) => {
          this.invite.set(invite);
          this.participants.set(sessions.participants);
          this.ownerSessionId.set(ownerSessionId);
          this.applySync(sessions, groupStatus);
          this.loadState.set('ready');
          if (this.isOpen()) this.startSync();
        },
        error: (error: unknown) => {
          const notFound = error instanceof HttpErrorResponse && error.status === 404;
          this.loadState.set(notFound ? 'not-found' : 'error');
        },
      });
  }

  /** Refresh: синхронізуватись зараз. Якщо запит уже йде — новий не стартує. */
  refresh(): void {
    if (this.syncing()) return;
    this.sync$().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }

  openConfirm(): void {
    if (this.canSubmit()) this.confirmOpen.set(true);
  }

  closeConfirm(): void {
    if (!this.submitting()) this.confirmOpen.set(false);
  }

  submit(): void {
    const invite = this.invite();
    if (!invite || this.submitting()) return;

    this.submitting.set(true);
    this.groupOrders
      .submit$(invite.GroupReservationId, this.reservationId)
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.status.set('Submitted');
          this.stopSync();
          this.confirmOpen.set(false);
          this.toast.success('Pre-order sent to the restaurant');
        },
        // toast з текстом помилки вже показує tokenInterceptor
        error: () => this.confirmOpen.set(false),
      });
  }

  private startSync(): void {
    // exhaustMap: поки попередній запит не відповів, тік таймера пропускається
    this.syncTimer = interval(SYNC_INTERVAL_MS)
      .pipe(
        exhaustMap(() => this.sync$()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private stopSync(): void {
    this.syncTimer?.unsubscribe();
    this.syncTimer = null;
  }

  /** Тихий запит кошиків і статусу: без лоадера і без toast, при помилці лишаються останні дані. */
  private sync$(): Observable<unknown> {
    const invite = this.invite();
    if (!invite) return EMPTY;
    const groupId = invite.GroupReservationId;

    return defer(() => {
      this.syncing.set(true);
      return forkJoin({
        sessions: this.groupOrders.getSessions$(groupId, { silent: true }),
        groupStatus: this.groupOrders.getStatus$(groupId, { silent: true }),
      });
    }).pipe(
      tap(({ sessions, groupStatus }) => {
        // відповідь, що прийшла вже після Submit, не повертає сторінку в Open
        if (!this.isOpen()) return;
        this.applySync(sessions, groupStatus);
        if (!this.isOpen()) this.stopSync();
      }),
      catchError(() => {
        this.refreshFailed.set(true);
        return EMPTY;
      }),
      finalize(() => this.syncing.set(false))
    );
  }

  private applySync(sessions: GroupSessionsResponse, groupStatus: GroupStatusResponse): void {
    this.participants.set(sessions.participants);
    this.status.set(groupStatus.status);
    this.lastUpdated.set(new Date());
    this.refreshFailed.set(false);
  }
}
