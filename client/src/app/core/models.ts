/**
 * Wire types mirroring the API's DTOs and enums.
 *
 * The API serializes every enum as a string (`JsonStringEnumConverter`), so these are string unions
 * rather than numeric enums. Keeping them as unions means a status the API adds later is a compile
 * error at the point of use rather than a wrong number rendered in a table.
 */

export type PolicyType = 'Auto' | 'Home' | 'Life';

export const POLICY_TYPES: readonly PolicyType[] = ['Auto', 'Home', 'Life'];

export type PolicyStatus = 'Active' | 'Cancelled' | 'Expired';

export const POLICY_STATUSES: readonly PolicyStatus[] = ['Active', 'Cancelled', 'Expired'];

export type ClaimStatus = 'Pending' | 'Approved' | 'Denied';

export const CLAIM_STATUSES: readonly ClaimStatus[] = ['Pending', 'Approved', 'Denied'];

/** The envelope every list endpoint returns instead of a bare array. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export interface PolicyHolder {
  id: number;
  firstName: string;
  lastName: string;
  /** Masked by the API for callers not entitled to disclose the address. */
  email: string;
  updatedAt: string | null;
  updatedBy: string | null;
  /** Base64 rowversion; sends back on writes for optimistic concurrency. */
  rowVersion: string | null;
}

export interface Policy {
  id: number;
  policyNumber: string;
  premium: number;
  status: PolicyStatus;
  policyholderName: string;
  policyHolderId: number;
  type: PolicyType;
  coverageLimit: number | null;
  startDate: string;
  endDate: string;
  updatedAt: string | null;
  updatedBy: string | null;
  rowVersion: string | null;
}

export interface Claim {
  id: number;
  claimNumber: string;
  policyId: number;
  description: string;
  amount: number;
  status: ClaimStatus;
  filedAt: string;
  decisionDate: string | null;
  decidedBy: string | null;
  adjusterNotes: string | null;
  updatedAt: string | null;
  updatedBy: string | null;
  rowVersion: string | null;
}

export interface CreatePolicyHolder {
  firstName: string;
  lastName: string;
  email: string;
}

/** Every field is optional; the API rejects a body where all three are null. */
export interface UpdatePolicyHolder {
  firstName?: string;
  lastName?: string;
  email?: string;
}

export interface CreatePolicy {
  premium: number;
  policyHolderId: number;
  type: PolicyType;
  coverageLimit?: number | null;
  startDate: string;
  endDate: string;
}

/** At least one of `premium` or `status` is required. */
export interface UpdatePolicy {
  premium?: number;
  status?: PolicyStatus;
  rowVersion?: string;
}

export interface CreateClaim {
  policyId: number;
  amount: number;
  description: string;
}

export interface UpdateClaimStatus {
  status: ClaimStatus;
  notes?: string;
  decidedBy?: string;
  rowVersion?: string;
}

export interface ClaimStatusTotal {
  status: ClaimStatus;
  claimCount: number;
  totalAmount: number;
  averageAmount: number;
}

export interface OpenClaimsByStatusReport {
  statuses: ClaimStatusTotal[];
  totalOpenClaims: number;
  totalOpenAmount: number;
}

export interface PolicyTypeTotal {
  type: PolicyType;
  policyCount: number;
  totalPremium: number;
  averagePremium: number;
}

export interface PremiumByTypeReport {
  types: PolicyTypeTotal[];
  totalPolicies: number;
  totalPremium: number;
}

export interface HolderClaimsRatio {
  policyHolderId: number;
  policyholderName: string;
  policyCount: number;
  totalPremium: number;
  openClaimCount: number;
  totalOpenClaimAmount: number;
  claimsRatio: number;
}

/** The paging/filtering options every list endpoint accepts as query string parameters. */
export interface PageQuery {
  page?: number;
  pageSize?: number;
  sortBy?: string;
  descending?: boolean;
  status?: string;
}

export const DEFAULT_PAGE_SIZE = 25;
export const MAX_PAGE_SIZE = 100;
