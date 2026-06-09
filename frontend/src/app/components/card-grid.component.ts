import { CommonModule, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import { MtgApiService } from '../api/mtg-api.service';
import type { CardRecommendation } from '../api/api.types';

@Component({
  selector: 'mtg-card-grid',
  standalone: true,
  imports: [CommonModule, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div *ngIf="!cards?.length" class="text-muted text-sm py-8 text-center">
      No cards to show. Pick a commander and run a preset query.
    </div>

    <div class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5 gap-4">
      <article *ngFor="let card of cards; trackBy: trackByOracleId"
               class="bg-panel border border-border2 rounded-xl overflow-hidden flex flex-col">
        <img [src]="api.scryfallImageUrl(card.oracleId)"
             [alt]="card.name"
             loading="lazy"
             class="w-full aspect-[488/680] object-cover bg-panel2"
             (error)="onImageError($event)">

        <div class="p-3 flex flex-col gap-1">
          <h3 class="text-sm font-semibold text-accent leading-tight truncate">{{ card.name }}</h3>

          <div class="flex flex-wrap gap-1 text-[10px] mt-1">
            <span *ngIf="card.category"
                  class="px-1.5 py-0.5 rounded bg-panel2 border border-border2 text-muted">
              {{ card.category }}
            </span>
            <span *ngIf="card.inclusionPct != null"
                  class="px-1.5 py-0.5 rounded bg-accent/10 border border-accent/30 text-accent">
              {{ card.inclusionPct | number:'1.0-1' }}%
            </span>
            <span *ngIf="card.topCutAppearances != null"
                  class="px-1.5 py-0.5 rounded bg-accent2/10 border border-accent2/30 text-accent2">
              ▲{{ card.topCutAppearances }}
            </span>
          </div>

          <div class="text-[11px] text-muted mt-1 flex justify-between">
            <span *ngIf="card.synergyScore != null">syn {{ card.synergyScore | number:'1.0-2' }}</span>
            <span *ngIf="card.priceEur != null"
                  class="text-warn font-medium">€{{ card.priceEur | number:'1.2-2' }}</span>
            <span *ngIf="card.priceEur == null" class="text-muted/70">no price</span>
          </div>
        </div>
      </article>
    </div>
  `,
})
export class CardGridComponent {
  @Input() cards: CardRecommendation[] | null = [];

  readonly api = inject(MtgApiService);

  trackByOracleId = (_: number, c: CardRecommendation) => c.oracleId;

  onImageError(ev: Event) {
    (ev.target as HTMLImageElement).src =
      'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 488 680">' +
      '<rect width="100%" height="100%" fill="%2318253c"/>' +
      '<text x="50%" y="50%" font-family="sans-serif" font-size="32" fill="%2393a3bd"' +
      ' text-anchor="middle" dy=".3em">no image</text></svg>';
  }
}
