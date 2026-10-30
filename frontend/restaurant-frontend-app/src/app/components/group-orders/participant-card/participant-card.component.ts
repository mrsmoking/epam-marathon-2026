import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, linkedSignal } from '@angular/core';
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

  // рахується саме, коли змінюється participant
  readonly title = computed(() => {
    const person = this.participant();
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

  toggle(): void {
    this.expanded.update((isExpanded) => !isExpanded);
  }
}
