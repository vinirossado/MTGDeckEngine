import { CommonModule, DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { catchError, of, tap } from 'rxjs';
import { MtgApiService } from './api/mtg-api.service';
import type {
  BudgetDeck,
  CardRecommendation,
  CommanderMeta,
  CommanderPick,
  CommanderSummary,
  DeckOption,
  FormatMeta,
  FormatSummary,
  SavedDeck,
  SavedDeckSummary,
} from './api/api.types';
import { CardGridComponent } from './components/card-grid.component';

interface Preset {
  key: string;
  label: string;
  description: string;
  run: () => void;
}

type Mode = 'commander' | 'format' | 'discover';

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

  // null = no bracket cap (build the strongest deck the budget allows).
  readonly maxBracket  = signal<number | null>(null);
  readonly deckName    = signal<string>('');
  readonly savedDecks  = signal<SavedDeckSummary[]>([]);
  readonly showSaved   = signal<boolean>(false);
  readonly showDecklist = signal<boolean>(false);

  // ---- the bracket x strategy grid ----
  readonly options        = signal<DeckOption[]>([]);
  readonly selectedOption = signal<DeckOption | null>(null);
  readonly copyLabel    = signal<string>('Copy decklist');

  /**
   * The deck rendered as plain "N Card Name" text, commander in a trailing
   * block. Built client-side from the deck already on screen so the button is
   * instant and always matches what is displayed — asking the API to rebuild
   * could return a different list, since prices move between calls.
   */
  readonly decklistText = computed(() => {
    const d = this.deck();
    if (!d) return '';

    const counts = new Map<string, number>();
    for (const card of d.cards) {
      const name = (card.name ?? '').trim();
      if (!name) continue;
      counts.set(name, (counts.get(name) ?? 0) + 1);
    }

    const lines = [...counts.entries()]
      .sort(([a], [b]) => a.localeCompare(b, 'en'))
      .map(([name, n]) => `${n} ${name}`);

    // The blank line before the commander is what tells importers it belongs in
    // the command zone rather than the 99.
    const commander = this.commanderName();
    return commander
      ? `${lines.join('\n')}\n\n1 ${commander}\n`
      : `${lines.join('\n')}\n`;
  });

  readonly decklistLineCount = computed(
    () => this.decklistText().split('\n').filter(l => l.trim().length > 0).length,
  );

  /**
   * Printed name of the deck's commander. Comes from the API response rather
   * than the commander dropdown: that list is capped at 500 and sorted by play
   * count, so a low-play commander is simply absent from it and the export
   * would silently lose its command-zone block.
   */
  private commanderName(): string | null {
    const d = this.deck();
    if (d?.commanderName) return d.commanderName;
    const slug = d?.commanderSlug ?? this.slug();
    return this.commanders().find(c => c.commanderSlug === slug)?.name ?? null;
  }

  toggleDecklist(): void {
    this.showDecklist.set(!this.showDecklist());
  }

  async copyDecklist(): Promise<void> {
    const text = this.decklistText();
    if (!text) return;
    try {
      await navigator.clipboard.writeText(text);
      this.copyLabel.set('Copied ✓');
    } catch {
      // Clipboard API needs a secure context and permission; fall back to
      // showing the list so the user can select it manually.
      this.showDecklist.set(true);
      this.copyLabel.set('Copy blocked — select below');
    }
    setTimeout(() => this.copyLabel.set('Copy decklist'), 2500);
  }

  downloadDecklist(): void {
    const text = this.decklistText();
    if (!text) return;
    const slug = this.deck()?.commanderSlug ?? 'deck';
    const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${slug}.txt`;
    a.click();
    URL.revokeObjectURL(url);
  }

  // ---- discover: which commander should I build? ----
  readonly picks           = signal<CommanderPick[]>([]);
  readonly discoverBracket = signal<number | null>(3);
  readonly discoverBudget  = signal<number | null>(200);
  readonly minDeckCount    = signal<number>(3);

  readonly bracketOptions = [
    { value: null, label: 'Any bracket' },
    { value: 1, label: '1 — Exhibition' },
    { value: 2, label: '2 — Core' },
    { value: 3, label: '3 — Upgraded' },
    { value: 4, label: '4 — Optimized' },
    { value: 5, label: '5 — cEDH' },
  ];

  readonly title = computed(() => {
    switch (this.mode()) {
      case 'commander': return `MTG Deck Engine — ${this.slug() || '—'}`;
      case 'format':    return `MTG Deck Engine — ${this.format() || '—'} format`;
      default:          return 'MTG Deck Engine — find a commander';
    }
  });

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
    this.refreshSavedDecks();
  }

  setDiscoverBracket(raw: string): void {
    this.discoverBracket.set(raw === '' || raw === 'null' ? null : Number(raw));
  }

  runDiscover(): void {
    this.loading.set(true);
    this.status.set('Searching…');
    this.api.discoverCommanders({
      maxBracket:   this.discoverBracket(),
      maxBudgetEur: this.discoverBudget(),
      minDeckCount: this.minDeckCount(),
      limit:        30,
    }).pipe(
      tap(rows => {
        this.picks.set(rows);
        this.status.set(rows.length
          ? `${rows.length} commanders`
          : 'No commander matches that bracket and budget.');
      }),
      catchError(err => {
        this.status.set(`Error: ${err?.error ?? err?.message ?? err}`);
        this.picks.set([]);
        return of([] as CommanderPick[]);
      }),
    ).subscribe(() => this.loading.set(false));
  }

  /** Jump from a discovered commander straight into building a deck for it. */
  buildFromPick(pick: CommanderPick): void {
    // Partner pairs are keyed "a+b"; the builder works off a single slug, so
    // take the first half rather than sending it a key it cannot resolve.
    this.slug.set(pick.commanderSlug.split('+')[0]);
    this.mode.set('commander');
    this.picks.set([]);
    this.fetchMeta();
    if (pick.minDeckPriceEur) this.budgetEur.set(Math.ceil(pick.minDeckPriceEur));
    this.maxBracket.set(pick.estimatedBracket);
  }

  setMaxBracket(raw: string): void {
    this.maxBracket.set(raw === '' || raw === 'null' ? null : Number(raw));
  }

  // ---- saved decks ----

  refreshSavedDecks(): void {
    this.api.listSavedDecks()
      .pipe(catchError(() => of([] as SavedDeckSummary[])))
      .subscribe(list => this.savedDecks.set(list));
  }

  toggleSaved(): void {
    this.showSaved.set(!this.showSaved());
    if (this.showSaved()) this.refreshSavedDecks();
  }

  /**
   * Build and save in a single request. Doing it in two calls would rebuild the
   * deck server-side and could persist a different list than the one on screen,
   * since prices and tournament data move between calls.
   */
  buildAndSave(): void {
    if (!this.slug()) return;
    const total = this.budgetEur();
    if (!(total > 0)) {
      this.status.set('Enter a budget greater than EUR 0.');
      return;
    }
    this.loading.set(true);
    this.status.set('Building and saving...');
    this.api.buildAndSave({
      commanderSlug:  this.slug(),
      totalBudgetEur: total,
      maxBracket:     this.maxBracket(),
      name:           this.deckName().trim() || null,
    }).pipe(
      tap(d => {
        this.cards.set(d.cards);
        this.deck.set({
          commanderSlug: d.commanderSlug,
          totalPriceEur: d.totalPriceEur,
          cardCount:     d.cardCount,
          cards:         d.cards,
          bracket:       d.bracket,
          commanderName: d.commanderName,
        });
        const bracket = d.bracket ? ` - Bracket ${d.bracket.level} (${d.bracket.label})` : '';
        this.status.set(`Saved "${d.name}" - ${d.cardCount} cards - EUR ${d.totalPriceEur.toFixed(2)}${bracket}`);
        this.deckName.set('');
        this.refreshSavedDecks();
        this.showSaved.set(true);
      }),
      catchError(err => {
        this.status.set(`Save failed: ${err?.error ?? err?.message ?? err}`);
        return of(null);
      }),
    ).subscribe(() => this.loading.set(false));
  }

  openSavedDeck(id: string): void {
    this.loading.set(true);
    this.status.set('Loading deck...');
    this.api.getSavedDeck(id).pipe(
      tap((d: SavedDeck) => {
        this.slug.set(d.commanderSlug);
        this.cards.set(d.cards);
        this.deck.set({
          commanderSlug: d.commanderSlug,
          totalPriceEur: d.totalPriceEur,
          cardCount:     d.cardCount,
          cards:         d.cards,
          bracket:       d.bracket,
          commanderName: d.commanderName,
        });
        const bracket = d.bracket ? ` - Bracket ${d.bracket.level}` : '';
        this.status.set(`${d.name} - ${d.cardCount} cards - EUR ${d.totalPriceEur.toFixed(2)}${bracket}`);
      }),
      catchError(err => {
        this.status.set(`Error: ${err?.message ?? err}`);
        return of(null);
      }),
    ).subscribe(() => this.loading.set(false));
  }

  deleteSavedDeck(id: string, event: Event): void {
    event.stopPropagation();
    this.api.deleteSavedDeck(id).pipe(
      tap(() => {
        this.status.set('Deck deleted.');
        this.refreshSavedDecks();
      }),
      catchError(err => {
        this.status.set(`Delete failed: ${err?.message ?? err}`);
        return of(null);
      }),
    ).subscribe();
  }

  setMode(m: Mode): void {
    if (this.mode() === m) return;
    this.mode.set(m);
    this.cards.set([]);
    this.deck.set(null);
    this.meta.set(null);
    this.formatMeta.set(null);
    this.status.set('');
    this.picks.set([]);
    if (m === 'commander') this.fetchMeta();
    else if (m === 'format') this.fetchFormatMeta();
    else this.runDiscover();
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

  /**
   * Fetch the grid rather than a single deck. Twelve builds, each needing a
   * bracket call, so this takes a few seconds — the button says so.
   */
  runCompareOptions(): void {
    if (!this.slug()) return;
    const total = this.budgetEur();
    if (!(total > 0)) {
      this.status.set('Enter a budget greater than EUR 0.');
      return;
    }
    this.loading.set(true);
    this.cards.set([]);
    this.deck.set(null);
    this.options.set([]);
    this.selectedOption.set(null);
    this.status.set('Building options… (a few seconds)');

    this.api.buildDeckOptions(this.slug(), total).pipe(
      tap(rows => {
        this.options.set(rows);
        this.status.set(rows.length
          ? `${rows.length} options across ${new Set(rows.map(r => r.bracket)).size} brackets`
          : 'No options could be built within that budget.');
      }),
      catchError(err => {
        this.status.set(`Error: ${err?.error ?? err?.message ?? err}`);
        return of([] as DeckOption[]);
      }),
    ).subscribe(() => this.loading.set(false));
  }

  /** Show one option's actual card list in the grid below. */
  pickOption(option: DeckOption): void {
    this.selectedOption.set(option);
    this.cards.set(option.cards);
    this.deck.set({
      commanderSlug: this.slug(),
      totalPriceEur: option.totalPriceEur,
      cardCount:     option.cardCount,
      cards:         option.cards,
      bracket:       option.bracketDetail,
      commanderName: option.commanderName,
    });
    this.status.set(
      `${option.strategyName} · Bracket ${option.bracket} · ` +
      `${option.cardCount} cards · EUR ${option.totalPriceEur.toFixed(2)}`);
  }

  /** Distinct brackets present, so the template can group without a pipe. */
  bracketsInOptions(): number[] {
    return [...new Set(this.options().map(o => o.bracket))].sort((a, b) => a - b);
  }

  optionsForBracket(bracket: number): DeckOption[] {
    return this.options().filter(o => o.bracket === bracket);
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
    this.options.set([]);
    this.status.set('Building…');
    this.api.buildDeck(this.slug(), total, perCardCap, this.maxBracket())
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
