import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type {
  BudgetDeck,
  CardRecommendation,
  CommanderMeta,
  CommanderSummary,
  FormatMeta,
  FormatStaple,
  FormatSummary,
  IngestSummary,
  RecommendationFilters,
  CommanderPick,
  SavedDeck,
  SavedDeckSummary,
  BuildAndSaveRequest,
} from './api.types';

@Injectable({ providedIn: 'root' })
export class MtgApiService {
  private readonly http = inject(HttpClient);
  private readonly base = 'http://localhost:5050/api';

  listCommanders(limit = 100): Observable<CommanderSummary[]> {
    return this.http.get<CommanderSummary[]>(`${this.base}/commanders`, {
      params: new HttpParams().set('limit', limit),
    });
  }

  listFormats(): Observable<FormatSummary[]> {
    return this.http.get<FormatSummary[]>(`${this.base}/formats`);
  }

  formatMeta(format: string): Observable<FormatMeta> {
    return this.http.get<FormatMeta>(
      `${this.base}/formats/${encodeURIComponent(format)}/meta`,
    );
  }

  formatStaples(
    format: string,
    opts: { maxPriceEur?: number; maxPlacement?: number; minDeckCount?: number; limit?: number } = {},
  ): Observable<FormatStaple[]> {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(opts)) {
      if (v != null) p = p.set(k, String(v));
    }
    return this.http.get<FormatStaple[]>(
      `${this.base}/formats/${encodeURIComponent(format)}/staples`,
      { params: p },
    );
  }

  recommendations(slug: string, f: RecommendationFilters = {}): Observable<CardRecommendation[]> {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(f)) {
      if (v !== undefined && v !== null) p = p.set(k, String(v));
    }
    return this.http.get<CardRecommendation[]>(
      `${this.base}/commanders/${encodeURIComponent(slug)}/recommendations`,
      { params: p },
    );
  }

  meta(slug: string): Observable<CommanderMeta> {
    return this.http.get<CommanderMeta>(
      `${this.base}/commanders/${encodeURIComponent(slug)}/meta`,
    );
  }

  buildDeck(
    slug: string,
    totalBudgetEur: number,
    maxCardPriceEur?: number,
    maxBracket?: number | null,
  ): Observable<BudgetDeck> {
    let p = new HttpParams().set('totalBudgetEur', totalBudgetEur);
    if (maxCardPriceEur != null) p = p.set('maxCardPriceEur', maxCardPriceEur);
    if (maxBracket != null) p = p.set('maxBracket', maxBracket);
    return this.http.get<BudgetDeck>(
      `${this.base}/commanders/${encodeURIComponent(slug)}/build-deck`,
      { params: p },
    );
  }

  /** Inverse of the builder: which commanders suit this bracket and budget? */
  discoverCommanders(
    opts: { maxBracket?: number | null; maxBudgetEur?: number | null; minDeckCount?: number; limit?: number } = {},
  ): Observable<CommanderPick[]> {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(opts)) {
      if (v != null) p = p.set(k, String(v));
    }
    return this.http.get<CommanderPick[]>(`${this.base}/commanders/discover`, { params: p });
  }

  // ---- saved decks ----

  listSavedDecks(commander?: string): Observable<SavedDeckSummary[]> {
    let p = new HttpParams();
    if (commander) p = p.set('commander', commander);
    return this.http.get<SavedDeckSummary[]>(`${this.base}/decks`, { params: p });
  }

  getSavedDeck(id: string): Observable<SavedDeck> {
    return this.http.get<SavedDeck>(`${this.base}/decks/${encodeURIComponent(id)}`);
  }

  buildAndSave(req: BuildAndSaveRequest): Observable<SavedDeck> {
    return this.http.post<SavedDeck>(`${this.base}/decks/build-and-save`, req);
  }

  deleteSavedDeck(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/decks/${encodeURIComponent(id)}`);
  }

  ingest(slug: string): Observable<IngestSummary> {
    return this.http.post<IngestSummary>(
      `${this.base}/commanders/${encodeURIComponent(slug)}/ingest`,
      {},
    );
  }

  // Scryfall — we don't keep card images in our store, just oracle ids.
  //
  // /cards/{id} expects Scryfall's *card id*, NOT the oracle id (those are
  // different identifiers — oracle id is stable across printings, card id
  // is per-printing). The right shape to fetch by oracle id is
  // /cards/search?q=oracleid:... with format=image; Scryfall 302s to the
  // image CDN which the browser caches normally.
  scryfallImageUrl(oracleId: string): string {
    const q = encodeURIComponent(`oracleid:${oracleId}`);
    return `https://api.scryfall.com/cards/search?q=${q}` +
      `&format=image&version=normal&unique=cards`;
  }
}
