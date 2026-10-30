import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { GROUP_ORDER_PREVIEW } from '@app/shared/mocks/group-orders/group-order.preview';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';

@Component({
  selector: 'app-group-order-page',
  imports: [ParticipantCardComponent, DatePipe, CurrencyPipe],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent {
  // поки що статичні дані; на рівні 2 їх замінить відповідь API
  readonly invite = signal(GROUP_ORDER_PREVIEW.invite);
  readonly participants = signal(GROUP_ORDER_PREVIEW.sessions.participants);
  readonly grandTotal = signal(GROUP_ORDER_PREVIEW.sessions.total);

  readonly guestsLabel = computed(() => `${this.participants().length} of ${this.invite().MaxGuestCount} guests`);
}
