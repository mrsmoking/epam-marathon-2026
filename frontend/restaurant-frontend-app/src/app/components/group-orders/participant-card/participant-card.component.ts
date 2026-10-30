import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, output } from '@angular/core';
import { Participant } from '@app/models/group-orders/participant.model';

@Component({
  selector: 'app-participant-card',
  imports: [CurrencyPipe],
  templateUrl: './participant-card.component.html',
  styleUrl: './participant-card.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ParticipantCardComponent {
  // що картка отримує ззовні — як параметр функції
  readonly participant = input.required<Participant>();
  // власна картка Owner (participant.id === guestSessionId)
  readonly isMe = input(false);
  // «Remove Guest» показує сторінка: лише для гостей і лише поки група Open
  readonly canRemove = input(false);
  readonly removeClick = output<Participant>();

  // рахується саме, коли змінюється participant
  readonly title = computed(() => {
    const person = this.participant();
    if (this.isMe()) return 'My Pre-Order';
    return person.type === 'Owner' ? `Owner: ${person.name}` : `Guest: ${person.name}`;
  });

  // Owner розгорнутий, гості згорнуті. Скидається лише при зміні типу,
  // тож оновлення даних (нові об'єкти participant) не згортає відкриту картку.
  readonly expanded = linkedSignal({
    source: () => this.participant().type,
    computation: (type) => type === 'Owner',
  });

  readonly itemsLabel = computed(() => {
    const count = this.participant().items.length;
    return count === 1 ? '1 item' : `${count} items`;
  });

  readonly bodyId = computed(() => `participant-${this.participant().id}-items`);

  readonly toggleLabel = computed(
    () => `${this.expanded() ? 'Hide' : 'Show'} ${this.participant().name}'s pre-order`
  );

  readonly removeLabel = computed(() => `Remove ${this.participant().name}`);

  toggle(): void {
    this.expanded.update((isExpanded) => !isExpanded);
  }
}
