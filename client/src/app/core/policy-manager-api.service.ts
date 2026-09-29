import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  Claim,
  ClaimStatus,
  CreateClaim,
  CreatePolicy,
  CreatePolicyHolder,
  HolderClaimsRatio,
  OpenClaimsByStatusReport,
  PagedResult,
  PageQuery,
  Policy,
  PolicyHolder,
  PremiumByTypeReport,
  UpdateClaimStatus,
  UpdatePolicy,
  UpdatePolicyHolder,
} from './models';

/**
 * Every call the UI makes to the API, in one place.
 *
 * The API is versioned but additive: unversioned routes still exist and still mean 1.0
 * (`Program.cs` sets `AssumeDefaultVersionWhenUnspecified`), so the paths here carry no version
 * segment. That is a choice to revisit only when a second version ships, not now.
 */
@Injectable({ providedIn: 'root' })
export class PolicyManagerApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  // ── Policyholders ──────────────────────────────────────────────────────────

  listPolicyHolders(query: PageQuery = {}): Observable<PagedResult<PolicyHolder>> {
    return this.http.get<PagedResult<PolicyHolder>>(`${this.base}/policyholders`, {
      params: toParams(query),
    });
  }

  getPolicyHolder(id: number): Observable<PolicyHolder> {
    return this.http.get<PolicyHolder>(`${this.base}/policyholders/${id}`);
  }

  createPolicyHolder(dto: CreatePolicyHolder): Observable<PolicyHolder> {
    return this.http.post<PolicyHolder>(`${this.base}/policyholders`, dto);
  }

  updatePolicyHolder(id: number, dto: UpdatePolicyHolder): Observable<PolicyHolder> {
    return this.http.put<PolicyHolder>(`${this.base}/policyholders/${id}`, dto);
  }

  /** Soft delete. The row stays; a deleted holder's policies keep resolving. */
  deletePolicyHolder(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/policyholders/${id}`);
  }

  restorePolicyHolder(id: number): Observable<PolicyHolder> {
    return this.http.patch<PolicyHolder>(`${this.base}/policyholders/${id}/restore`, null);
  }

  /** A holder's own policies, with the same paging/sorting plus a `status` filter. */
  searchHolderPolicies(id: number, query: PageQuery = {}): Observable<PagedResult<Policy>> {
    return this.http.get<PagedResult<Policy>>(`${this.base}/policyholders/${id}/policies`, {
      params: toParams(query),
    });
  }

  // ── Policies ───────────────────────────────────────────────────────────────

  listPolicies(query: PageQuery = {}): Observable<PagedResult<Policy>> {
    return this.http.get<PagedResult<Policy>>(`${this.base}/policies`, {
      params: toParams(query),
    });
  }

  getPolicy(id: number): Observable<Policy> {
    return this.http.get<Policy>(`${this.base}/policies/${id}`);
  }

  createPolicy(dto: CreatePolicy): Observable<Policy> {
    return this.http.post<Policy>(`${this.base}/policies`, dto);
  }

  updatePolicy(id: number, dto: UpdatePolicy): Observable<Policy> {
    return this.http.put<Policy>(`${this.base}/policies/${id}`, dto);
  }

  deletePolicy(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/policies/${id}`);
  }

  /** A policy's own claims, with the same paging/sorting plus a `status` filter. */
  searchPolicyClaims(id: number, query: PageQuery = {}): Observable<PagedResult<Claim>> {
    return this.http.get<PagedResult<Claim>>(`${this.base}/policies/${id}/claims`, {
      params: toParams(query),
    });
  }

  // ── Claims ─────────────────────────────────────────────────────────────────

  listClaims(query: PageQuery = {}): Observable<PagedResult<Claim>> {
    return this.http.get<PagedResult<Claim>>(`${this.base}/claims`, {
      params: toParams(query),
    });
  }

  getClaim(id: number): Observable<Claim> {
    return this.http.get<Claim>(`${this.base}/claims/${id}`);
  }

  createClaim(dto: CreateClaim): Observable<Claim> {
    return this.http.post<Claim>(`${this.base}/claims`, dto);
  }

  /** Adjudication. An approved or denied claim is final and cannot be deleted afterwards. */
  updateClaimStatus(id: number, dto: UpdateClaimStatus): Observable<void> {
    return this.http.patch<void>(`${this.base}/claims/${id}/status`, dto);
  }

  /** Soft delete. An already-adjudicated claim answers 409. */
  deleteClaim(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/claims/${id}`);
  }

  restoreClaim(id: number): Observable<Claim> {
    return this.http.patch<Claim>(`${this.base}/claims/${id}/restore`, null);
  }

  // ── Reports ────────────────────────────────────────────────────────────────

  openClaimsByStatus(): Observable<OpenClaimsByStatusReport> {
    return this.http.get<OpenClaimsByStatusReport>(`${this.base}/reports/open-claims-by-status`);
  }

  premiumByType(): Observable<PremiumByTypeReport> {
    return this.http.get<PremiumByTypeReport>(`${this.base}/reports/premium-by-type`);
  }

  claimsRatioPerHolder(query: PageQuery = {}): Observable<PagedResult<HolderClaimsRatio>> {
    return this.http.get<PagedResult<HolderClaimsRatio>>(
      `${this.base}/reports/claims-ratio-per-holder`,
      { params: toParams(query) },
    );
  }

  // ── Health ─────────────────────────────────────────────────────────────────

  /** Unauthenticated, so it is the one call that answers whether the API is reachable at all. */
  health(): Observable<unknown> {
    return this.http.get(`${this.base.replace('/api', '')}/health`);
  }
}

/**
 * Builds query parameters, dropping the ones the caller left unset.
 *
 * Sending `page=undefined` would reach the API as the literal string "undefined", which binds
 * cleanly to a string and then fails the model's own validation — so an omitted option has to be
 * omitted from the query, not sent empty.
 */
function toParams(query: PageQuery): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      params = params.set(key, String(value));
    }
  }
  return params;
}

export type { ClaimStatus };
