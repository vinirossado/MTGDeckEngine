// Mirror of the API's response shapes. Kept hand-written and small —
// nothing in here that the API doesn't actually return today.

export interface CardRecommendation {
  oracleId: string;
  name: string;
  category: string | null;
  inclusionPct: number | null;
  synergyScore: number | null;
  priceEur: number | null;
  topCutAppearances: number | null;
  imageUrl: string | null;
  typeLine?: string | null;
  colorIdentity?: string | null;
  tournamentWins?: number | null;
  tournamentLosses?: number | null;
  tournamentGames?: number | null;
  winRate?: number | null;
}

export interface CommanderMeta {
  commanderSlug: string;
  tournamentEntryCount: number;
  topCutCount: number;
  winRate: number | null;
  conversionRate: number | null;
  metaShare: number | null;
  top4DeckCount: number;
  top16DeckCount: number;
  latestTopCutDate: string | null;
}

export interface DeckBracket {
  level: number;
  label: string;
  gameChangerCount: number;
  gameChangersFound: string[];
  hasMassLandDenial: boolean;
  hasExtraTurns: boolean;
  reasons: string[];
  isEstimate: boolean;
}

export interface BudgetDeck {
  commanderSlug: string;
  totalPriceEur: number;
  cardCount: number;
  cards: CardRecommendation[];
  bracket: DeckBracket | null;
  commanderName?: string | null;
}

export interface CommanderSummary {
  commanderSlug: string;
  name: string;
  tournamentEntryCount: number;
  topCutCount: number;
}

export interface FormatSummary {
  format: string;
  tournamentCount: number;
  deckCount: number;
  entryCount: number;
}

export interface FormatMeta {
  format: string;
  tournamentCount: number;
  deckCount: number;
  entryCount: number;
  top4DeckCount: number;
  top16DeckCount: number;
  latestTournamentDate: string | null;
}

export interface FormatStaple {
  oracleId: string;
  name: string;
  priceEur: number | null;
  deckCount: number;
  imageUrl: string | null;
}

export interface IngestSummary {
  commanderSlug: string;
  edhrecIngested: boolean;
  edhTop16Ingested: boolean;
  durationMs: number;
  error: string | null;
}

export type RecommendationSource = 'Edhrec' | 'Tournament' | 'All';

export interface RecommendationFilters {
  maxPriceEur?: number;
  minInclusion?: number;
  minSynergy?: number;
  excludeLands?: boolean;
  excludeBasicLands?: boolean;
  maxPlacement?: number;
  minTopCutAppearances?: number;
  source?: RecommendationSource;
  limit?: number;
}

export interface SavedDeckSummary {
  id: string;
  name: string;
  commanderSlug: string;
  totalPriceEur: number;
  cardCount: number;
  savedAt: string;
  bracketLevel: number | null;
  bracketLabel: string | null;
  budgetEur: number | null;
}

export interface SavedDeck {
  id: string;
  name: string;
  commanderSlug: string;
  totalPriceEur: number;
  cardCount: number;
  savedAt: string;
  cards: CardRecommendation[];
  bracket: DeckBracket | null;
  notes: string | null;
  budgetEur: number | null;
  commanderName: string | null;
}

export interface BuildAndSaveRequest {
  commanderSlug: string;
  totalBudgetEur: number;
  maxBracket?: number | null;
  maxCardPriceEur?: number | null;
  minSynergy?: number | null;
  name?: string | null;
  notes?: string | null;
}

export interface CommanderPick {
  commanderSlug: string;
  name: string;
  deckCount: number;
  tournamentWins: number;
  tournamentLosses: number;
  winRate: number | null;
  adjustedWinRate: number;
  topCutCount: number;
  minDeckPriceEur: number | null;
  avgDeckPriceEur: number | null;
  maxGameChangers: number;
  estimatedBracket: number;
  imageUrl: string | null;
}
