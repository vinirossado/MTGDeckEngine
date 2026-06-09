import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type {
  BudgetDeck,
  CardRecommendation,
  CommanderMeta,
  CommanderSummary,
  FormatSummary,
  IngestSummary,
  RecommendationFilters,
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

  buildDeck(slug: string, totalBudgetEur: number, maxCardPriceEur?: number): Observable<BudgetDeck> {
    let p = new HttpParams().set('totalBudgetEur', totalBudgetEur);
    if (maxCardPriceEur != null) p = p.set('maxCardPriceEur', maxCardPriceEur);
    return this.http.get<BudgetDeck>(
      `${this.base}/commanders/${encodeURIComponent(slug)}/build-deck`,
      { params: p },
    );
  }

  ingest(slug: string): Observable<IngestSummary> {
    return this.http.post<IngestSummary>(
      `${this.base}/commanders/${encodeURIComponent(slug)}/ingest`,
      {},
    );
  }

  // Scryfall — we don't keep card images in our store, just oracle ids.
  // Hit Scryfall by oracle id for the image. They allow CORS, no auth, and
  // they rate-limit at ~10 req/sec which the browser respects naturally.
  scryfallImageUrl(oracleId: string): string {
    return `https://api.scryfall.com/cards/${oracleId}?format=image&version=normal`;
  }
}
