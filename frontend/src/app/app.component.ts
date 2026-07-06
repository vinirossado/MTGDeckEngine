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
  FormatMeta,
  FormatSummary,
} from './api/api.types';
import { CardGridComponent } from './components/card-grid.component';

interface Preset {
  key: string;
  label: string;
  description: string;
  run: () => void;
}

type Mode = 'commander' | 'format';

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
  readonly formats    = signal<FormatSummary[]>([]);
  readonly mode       = signal<Mode>('commander');
  readonly slug       = signal<string>('xyris-the-writhing-storm');
  readonly format     = signal<string>('EDH');
  readonly newSlug    = signal<string>('');
  readonly budgetEur  = signal<number>(100);
  readonly status     = signal<string>('');
  readonly meta       = signal<CommanderMeta | null>(null);
  readonly formatMeta = signal<FormatMeta | null>(null);
  readonly cards      = signal<CardRecommendation[]>([]);
  readonly deck       = signal<BudgetDeck | null>(null);
  readonly loading    = signal<boolean>(false);

  readonly title = computed(() =>
    this.mode() === 'commander'
      ? `MTG Deck Engine — ${this.slug() || '—'}`
      : `MTG Deck Engine — ${this.format() || '—'} format`,
  );

  // Preset queries shown as buttons. Each one calls a Phase 1+ endpoint
  // we built on the API side; the result populates `cards` or `deck`.
  // Filtered to the active mode (commander vs format) by `presets()` below.
  readonly commanderPresets: Preset[] = [
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
  ];

  readonly formatPresets: Preset[] = [
    {
      key: 'format-staples',
      label: 'Top 25 format staples',
      description: 'Most-played cards in this format (any placement).',
      run: () => this.runFormatStaples({ minDeckCount: 1, limit: 25 }),
    },
    {
      key: 'format-top8-under-10',
      label: 'Top 8 cards under €10',
      description: 'Cards in Top-8 finishing decks, capped at €10 each.',
      run: () =>
        this.runFormatStaples({ maxPlacement: 8, maxPriceEur: 10, minDeckCount: 1, limit: 25 }),
    },
    {
      key: 'format-budget-under-5',
      label: 'Budget gems under €5',
      description: 'Cheap cards that still appear in winning lists.',
      run: () =>
        this.runFormatStaples({ maxPriceEur: 5, minDeckCount: 2, limit: 30 }),
    },
  ];

  readonly presets = computed(() =>
    this.mode() === 'commander' ? this.commanderPresets : this.formatPresets,
  );

  constructor() {
    this.refreshCommanderList();
    this.refreshFormatList();
    this.fetchMeta();
  }

  setMode(m: Mode): void {
    if (this.mode() === m) return;
    this.mode.set(m);
    this.cards.set([]);
    this.deck.set(null);
    this.meta.set(null);
    this.formatMeta.set(null);
    this.status.set('');
    if (m === 'commander') this.fetchMeta();
    else this.fetchFormatMeta();
  }

  refreshFormatList(): void {
    this.api.listFormats().subscribe(list => this.formats.set(list));
  }

  pickFormat(f: string): void {
    if (!f) return;
    this.format.set(f);
    this.cards.set([]);
    this.deck.set(null);
    this.formatMeta.set(null);
    this.fetchFormatMeta();
  }

  fetchFormatMeta(): void {
    if (!this.format()) return;
    this.api.formatMeta(this.format()).pipe(catchError(() => of(null)))
      .subscribe(m => this.formatMeta.set(m));
  }

  runFormatStaples(opts: { maxPriceEur?: number; maxPlacement?: number; minDeckCount?: number; limit?: number } = {}): void {
    if (!this.format()) return;
    this.loading.set(true);
    this.deck.set(null);
    this.status.set('Loading…');
    this.api.formatStaples(this.format(), opts).pipe(
      tap(staples => {
        // Adapt FormatStaple → CardRecommendation so the existing grid renders it.
        const adapted: CardRecommendation[] = staples.map(s => ({
          oracleId: s.oracleId,
          name:     s.name,
          category: null,
          inclusionPct: null,
          synergyScore: null,
          priceEur: s.priceEur,
          topCutAppearances: s.deckCount,
          imageUrl: s.imageUrl,
        }));
        this.cards.set(adapted);
        this.status.set(`${adapted.length} cards`);
      }),
      catchError(err => {
        this.status.set(`Error: ${err?.message ?? err}`);
        this.cards.set([]);
        return of(null);
      }),
    ).subscribe(() => this.loading.set(false));
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

  runBuildDeck(budget?: number, perCardCap?: number): void {
    if (!this.slug()) return;
    const total = budget ?? this.budgetEur();
    if (!(total > 0)) {
      this.status.set('Enter a budget greater than €0.');
      return;
    }
    this.loading.set(true);
    this.cards.set([]);
    this.status.set('Building…');
    this.api.buildDeck(this.slug(), total, perCardCap)
      .pipe(
        tap(d => {
          this.deck.set(d);
          this.cards.set(d.cards);
          const bracket = d.bracket ? ` · Bracket ${d.bracket.level} (${d.bracket.label})` : '';
          this.status.set(`${d.cardCount} cards · €${d.totalPriceEur.toFixed(2)}${bracket}`);
        }),
        catchError(err => {
          this.status.set(`Error: ${err?.message ?? err}`);
          return of(null);
        }),
      )
      .subscribe(() => this.loading.set(false));
  }

  /**
   * Convert free-form input ("Jaheira, Friend of the Forest" or "JAHEIRA, friend of the forest")
   * into EDHREC's slug format ("jaheira-friend-of-the-forest"). Idempotent on
   * already-correct slugs.
   */
  private toSlug(input: string): string {
    return input
      .toLowerCase()
      .normalize('NFD').replace(/[̀-ͯ]/g, '')   // strip diacritics
      .replace(/['"]/g, '')                                // drop quotes
      .replace(/[^a-z0-9]+/g, '-')                         // collapse non-alphanum to -
      .replace(/^-+|-+$/g, '');                            // trim leading/trailing -
  }

  /** Phase 6a — pull EDHREC + EDHTop16 data for a new commander on demand. */
  ingestNew(): void {
    const slug = this.toSlug(this.newSlug().trim());
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
