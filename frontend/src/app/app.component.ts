import { CommonModule, DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { catchError, of, tap } from 'rxjs';
import { MtgApiService } from './api/mtg-api.service';
import type {
  BudgetDeck,
  CardRecommendation,
  CommanderMeta,
  CommanderSummary,
} from './api/api.types';
import { CardGridComponent } from './components/card-grid.component';

interface Preset {
  key: string;
  label: string;
  description: string;
  run: () => void;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule, CardGridComponent, DecimalPipe, PercentPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.component.html',
})
export class AppComponent {
  private readonly api = inject(MtgApiService);

  readonly commanders = signal<CommanderSummary[]>([]);
  readonly slug       = signal<string>('xyris-the-writhing-storm');
  readonly newSlug    = signal<string>('');
  readonly status     = signal<string>('');
  readonly meta       = signal<CommanderMeta | null>(null);
  readonly cards      = signal<CardRecommendation[]>([]);
  readonly deck       = signal<BudgetDeck | null>(null);
  readonly loading    = signal<boolean>(false);

  readonly title = computed(() => `MTG Deck Engine — ${this.slug() || '—'}`);

  // Preset queries shown as buttons. Each one calls a Phase 1+ endpoint
  // we built on the API side; the result populates `cards` or `deck`.
  readonly presets: Preset[] = [
    {
      key: 'top-staples',
      label: 'Top 25 staples (no basics)',
      description: 'Highest-inclusion cards, basic lands excluded.',
      run: () => this.runRecs({ excludeBasicLands: true, limit: 25 }),
    },
    {
      key: 'budget-under-5',
      label: 'Best under €5',
      description: 'Top-inclusion cards capped at €5 each.',
      run: () => this.runRecs({ maxPriceEur: 5, excludeBasicLands: true, limit: 25 }),
    },
    {
      key: 'top4-cedh',
      label: 'Top 4 tournament cards under €5',
      description: 'Cards from Top-4 finishing decks (tournament source).',
      run: () =>
        this.runRecs({
          source: 'Tournament',
          maxPlacement: 4,
          maxPriceEur: 5,
          excludeBasicLands: true,
          limit: 25,
        }),
    },
    {
      key: 'high-synergy',
      label: 'High-synergy archetype glue',
      description: "Cards more common in *this* commander's decks than the meta.",
      run: () => this.runRecs({ minSynergy: 0.4, excludeBasicLands: true, limit: 25 }),
    },
    {
      key: 'budget-deck-100',
      label: 'Build a €100 deck',
      description: 'Quota-aware greedy: 37 lands, 10 ramp, 10 draw, 8 removal…',
      run: () => this.runBuildDeck(100, 15),
    },
  ];

  constructor() {
    this.refreshCommanderList();
    this.fetchMeta();
  }

  refreshCommanderList(): void {
    this.api.listCommanders().subscribe(list => this.commanders.set(list));
  }

  pickCommander(s: string): void {
    if (!s) return;
    this.slug.set(s);
    this.cards.set([]);
    this.deck.set(null);
    this.meta.set(null);
    this.fetchMeta();
  }

  fetchMeta(): void {
    if (!this.slug()) return;
    this.api
      .meta(this.slug())
      .pipe(catchError(() => of(null)))
      .subscribe(m => this.meta.set(m));
  }

  runRecs(filters: Parameters<MtgApiService['recommendations']>[1]): void {
    if (!this.slug()) return;
    this.loading.set(true);
    this.deck.set(null);
    this.status.set('Loading…');
    this.api.recommendations(this.slug(), filters)
      .pipe(
        tap(rows => {
          this.cards.set(rows);
          this.status.set(`${rows.length} cards`);
        }),
        catchError(err => {
          this.status.set(`Error: ${err?.message ?? err}`);
          this.cards.set([]);
          return of([]);
        }),
      )
      .subscribe(() => this.loading.set(false));
  }

  runBuildDeck(budget: number, perCardCap: number): void {
    if (!this.slug()) return;
    this.loading.set(true);
    this.cards.set([]);
    this.status.set('Building…');
    this.api.buildDeck(this.slug(), budget, perCardCap)
      .pipe(
        tap(d => {
          this.deck.set(d);
          this.cards.set(d.cards);
          this.status.set(`${d.cardCount} cards · €${d.totalPriceEur.toFixed(2)}`);
        }),
        catchError(err => {
          this.status.set(`Error: ${err?.message ?? err}`);
          return of(null);
        }),
      )
      .subscribe(() => this.loading.set(false));
  }

  /** Phase 6a — pull EDHREC + EDHTop16 data for a new commander on demand. */
  ingestNew(): void {
    const slug = this.newSlug().trim();
    if (!slug) return;
    this.loading.set(true);
    this.status.set(`Ingesting ${slug}… (this can take 30s)`);
    this.api.ingest(slug)
      .pipe(
        tap(s => {
          this.status.set(
            `Ingested ${s.commanderSlug} in ${s.durationMs} ms` +
            (s.edhTop16Ingested ? '' : ' (EDHTop16 skipped)'),
          );
          this.newSlug.set('');
          this.refreshCommanderList();
          this.pickCommander(slug);
        }),
        catchError(err => {
          this.status.set(`Ingest failed: ${err?.error?.error ?? err?.message ?? err}`);
          return of(null);
        }),
      )
      .subscribe(() => this.loading.set(false));
  }
}
