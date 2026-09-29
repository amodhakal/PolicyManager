import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { throwError } from 'rxjs';

import { AuthService } from '../../core/auth.service';
import { ListState } from '../../core/list-state';
import { CLAIM_STATUSES, POLICY_STATUSES, PolicyStatus } from '../../core/models';
import { ApiError } from '../../core/problem-details';
import { PolicyManagerApi } from '../../core/policy-manager-api.service';
import { injectResource } from '../../core/resource';
import { Badge } from '../../shared/badge';
import { ErrorBanner } from '../../shared/error-banner';
import { Pager } from '../../shared/pager';

/**
 * One policy, its audit trail, and the claims filed against it.
 *
 * The claims live here rather than only on the claims list because a claim is meaningless on its
 * own: it is an amount requested against a specific policy's cover, and the coverage questions an
 * adjuster asks are all asked of the policy. Filing one is offered from the same page for the same
 * reason.
 *
 * The `rowVersion` is displayed but never parsed. It is an opaque base64 concurrency token; the
 * only correct thing to do with it is echo it back on the next write, and showing it is what lets
 * an operator correlate this row with a server log.
 */
@Component({
  selector: 'app-policy-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, FormsModule, RouterLink, Badge, ErrorBanner, Pager],
  template: `
    <p class="crumbs"><a routerLink="/policies">Policies</a></p>
    <h1>{{ policy()?.policyNumber ?? 'Policy' }}</h1>

    @if (policyResource.loading() && !policy()) {
      <p class="muted">Loading…</p>
    }

    @if (missingPolicy()) {
      <div class="card missing" role="alert">
        <p class="headline">There is no policy with that identifier.</p>
        <p class="muted">
          The API answered 404 for <code>{{ id() }}</code
          >. Policy identifiers are assigned by the server, so an identifier from another
          environment will not resolve here.
        </p>
        <a routerLink="/policies">Back to the policy list</a>
      </div>
    } @else {
      <app-error-banner [error]="policyResource.error()" />
    }

    @if (policy(); as current) {
      <div class="card">
        <dl class="facts">
          <div>
            <dt>Policy number</dt>
            <dd class="strong">{{ current.policyNumber }}</dd>
          </div>
          <div>
            <dt>Type</dt>
            <dd><app-badge [value]="current.type" /></dd>
          </div>
          <div>
            <dt>Status</dt>
            <dd><app-badge [value]="current.status" /></dd>
          </div>
          <div>
            <dt>Premium</dt>
            <dd class="numeric">{{ current.premium | number: '1.2-2' }}</dd>
          </div>
          <div>
            <dt>Coverage limit</dt>
            <dd class="numeric">
              @if (current.coverageLimit === null) {
                <span class="muted">no stated limit</span>
              } @else {
                {{ current.coverageLimit | number: '1.2-2' }}
              }
            </dd>
          </div>
          <div>
            <dt>Policyholder</dt>
            <dd>
              <a [routerLink]="['/policyholders', current.policyHolderId]">
                {{ current.policyholderName }}
              </a>
            </dd>
          </div>
          <div>
            <dt>Cover</dt>
            <dd>
              <span>{{ asDate(current.startDate) }}</span>
              <span aria-hidden="true"> – </span>
              <span>{{ asDate(current.endDate) }}</span>
            </dd>
          </div>
          <div>
            <dt>Last updated</dt>
            <dd>
              @if (current.updatedAt) {
                {{ asDateTime(current.updatedAt) }}
                @if (current.updatedBy) {
                  <span class="muted">by {{ current.updatedBy }}</span>
                }
              } @else {
                <span class="muted">never</span>
              }
            </dd>
          </div>
          <div>
            <dt>Row version</dt>
            <dd>
              <code class="token" [title]="current.rowVersion ?? ''">{{
                shortToken(current.rowVersion)
              }}</code>
            </dd>
          </div>
        </dl>
      </div>

      @if (canWrite()) {
        <form class="card" (ngSubmit)="save()">
          <h2>Edit</h2>
          <p class="muted note">
            Only what you change is sent, together with the row version above. If someone else saved
            this policy in the meantime, the write is refused rather than overwriting them.
          </p>

          @if (editError(); as failure) {
            <app-error-banner [error]="failure" />
          }
          @if (conflicted()) {
            <p class="conflict" role="alert">
              Someone else changed this policy after you loaded it, so your change was not applied.
              Reload the page to see their version before editing again.
            </p>
          }

          <div class="fields">
            <div class="field">
              <label for="edit-premium">Premium</label>
              <input
                id="edit-premium"
                name="premium"
                type="number"
                step="0.01"
                min="0.01"
                max="99999999.99"
                inputmode="decimal"
                [(ngModel)]="premiumDraft"
              />
              @if (fieldError('Premium'); as message) {
                <p class="field-error" role="alert">{{ message }}</p>
              }
            </div>
            <div class="field">
              <label for="edit-status">Status</label>
              <select id="edit-status" name="status" [(ngModel)]="statusDraft">
                @for (option of statuses; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>
              @if (fieldError('Status'); as message) {
                <p class="field-error" role="alert">{{ message }}</p>
              }
            </div>
          </div>

          <button type="submit" class="primary" [disabled]="!hasChanges() || saving()">
            {{ saving() ? 'Saving…' : 'Save changes' }}
          </button>
          @if (!hasChanges()) {
            <p class="muted hint">Nothing to save yet.</p>
          }
        </form>
      }

      @if (canDelete()) {
        <div class="card danger-zone">
          <h2>Cancel this policy</h2>
          <p class="muted note">
            This does not remove the row. The API sets the status to
            <strong>Cancelled</strong>, and a policy has no restore — unlike a soft-deleted
            policyholder, which can be brought back. Cancelling ends cover and cannot be undone from
            this application.
          </p>

          @if (deleteError(); as failure) {
            <app-error-banner [error]="failure" />
          }

          @if (confirmingDelete()) {
            <div class="confirm" role="alert">
              <p>Cancel policy {{ current.policyNumber }}? This cannot be undone here.</p>
              <button type="button" class="danger" (click)="cancelPolicy()" [disabled]="deleting()">
                {{ deleting() ? 'Cancelling…' : 'Yes, cancel it' }}
              </button>
              <button type="button" (click)="confirmingDelete.set(false)">Keep it</button>
            </div>
          } @else {
            <button type="button" class="danger" (click)="confirmingDelete.set(true)">
              Cancel this policy…
            </button>
          }
        </div>
      }

      <div class="card">
        <div class="claims-head">
          <h2>Claims</h2>
          <div class="claims-tools">
            <label class="filter">
              Status
              <select
                [value]="claimState.status() ?? ''"
                (change)="setClaimStatus($any($event.target).value)"
              >
                <option value="">All</option>
                @for (option of claimStatuses; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>
            </label>
            <button
              type="button"
              [title]="
                canFileClaim()
                  ? null
                  : 'Filing a claim needs an Admin or Adjuster token; this one is not eligible'
              "
              (click)="toggleClaimForm()"
            >
              {{ filingClaim() ? 'Close' : 'File a claim…' }}
            </button>
          </div>
        </div>

        @if (filingClaim()) {
          <form class="claim-form" (ngSubmit)="fileClaim()">
            <h3>File a claim against {{ current.policyNumber }}</h3>
            <p class="muted note">
              Only an active policy accepts claims, and the amount is measured against the cover
              remaining after the claims already on it.
            </p>

            @if (claimError(); as failure) {
              <app-error-banner [error]="failure" />
            }

            <div class="fields">
              <div class="field">
                <label for="claim-amount">Amount</label>
                <input
                  id="claim-amount"
                  name="amount"
                  type="number"
                  step="0.01"
                  min="0.01"
                  max="99999999.99"
                  inputmode="decimal"
                  [(ngModel)]="claimAmount"
                  required
                />
                @if (fieldError('Amount', claimError()); as message) {
                  <p class="field-error" role="alert">{{ message }}</p>
                }
              </div>
              <div class="field wide">
                <label for="claim-description">Description</label>
                <textarea
                  id="claim-description"
                  name="description"
                  rows="3"
                  [(ngModel)]="claimDescription"
                  required
                ></textarea>
                @if (fieldError('Description', claimError()); as message) {
                  <p class="field-error" role="alert">{{ message }}</p>
                }
              </div>
            </div>

            <button type="submit" class="primary" [disabled]="!claimIsValid() || filing()">
              {{ filing() ? 'Filing…' : 'File claim' }}
            </button>
          </form>
        }

        <app-error-banner [error]="claimsResource.error()" />

        @if (claimsResource.loading() && !claims()) {
          <p class="muted">Loading claims…</p>
        }

        @if (claims(); as page) {
          <table>
            <thead>
              <tr>
                <th scope="col">
                  <button type="button" class="link" (click)="sortClaimsBy('claimNumber')">
                    Claim number
                    @if (claimState.sortBy() === 'claimNumber') {
                      <span aria-hidden="true">{{ claimState.descending() ? '▼' : '▲' }}</span>
                    }
                  </button>
                </th>
                <th scope="col">Status</th>
                <th scope="col" class="numeric">
                  <button type="button" class="link" (click)="sortClaimsBy('amount')">
                    Amount
                    @if (claimState.sortBy() === 'amount') {
                      <span aria-hidden="true">{{ claimState.descending() ? '▼' : '▲' }}</span>
                    }
                  </button>
                </th>
                <th scope="col">
                  <button type="button" class="link" (click)="sortClaimsBy('filedAt')">
                    Filed
                    @if (claimState.sortBy() === 'filedAt') {
                      <span aria-hidden="true">{{ claimState.descending() ? '▼' : '▲' }}</span>
                    }
                  </button>
                </th>
                <th scope="col">Decided</th>
                <th scope="col" class="actions-heading">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (claim of page.items; track claim.id) {
                <tr>
                  <td>
                    <a [routerLink]="['/claims', claim.id]">{{ claim.claimNumber }}</a>
                  </td>
                  <td><app-badge [value]="claim.status" /></td>
                  <td class="numeric">{{ claim.amount | number: '1.2-2' }}</td>
                  <td>{{ asDate(claim.filedAt) }}</td>
                  <td>
                    @if (claim.decisionDate) {
                      {{ asDate(claim.decisionDate) }}
                      @if (claim.decidedBy) {
                        <span class="muted">by {{ claim.decidedBy }}</span>
                      }
                    } @else {
                      <span class="muted">—</span>
                    }
                  </td>
                  <td class="actions">
                    <a [routerLink]="['/claims', claim.id]">View</a>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="6" class="muted empty">
                    @if (claimState.status()) {
                      No {{ claimState.status() }} claims on this policy.
                    } @else {
                      No claims filed against this policy.
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>

          <app-pager
            [result]="page"
            (pageChange)="goToClaimPage($event)"
            (pageSizeChange)="setClaimPageSize($event)"
          />
        }
      </div>
    }
  `,
  styles: `
    .crumbs {
      margin: 0 0 0.25rem;
      font-size: 0.875rem;
    }
    .facts {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: 0.875rem 1.25rem;
      margin: 0;
    }
    .facts div {
      min-width: 0;
    }
    .facts dt {
      font-size: 0.75rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: #71717a;
      margin-bottom: 0.125rem;
    }
    .facts dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .strong {
      font-weight: 600;
      font-size: 1.0625rem;
    }
    .token {
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
      font-size: 0.75rem;
      background: #f4f4f5;
      border: 1px solid #e4e4e7;
      border-radius: 4px;
      padding: 0.0625rem 0.3125rem;
    }
    .missing .headline {
      margin: 0 0 0.5rem;
      font-weight: 600;
      color: #7a271a;
    }
    .note {
      margin-top: 0;
      font-size: 0.875rem;
      max-width: 60ch;
    }
    .fields {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: 0.75rem 1rem;
      margin-bottom: 1rem;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .field.wide {
      grid-column: 1 / -1;
    }
    .field label {
      font-weight: 500;
    }
    .field-error {
      margin: 0;
      font-size: 0.8125rem;
      color: #b42318;
    }
    .conflict {
      margin: 0 0 1rem;
      padding: 0.75rem 1rem;
      border: 1px solid #fedf89;
      border-left-width: 4px;
      border-radius: 4px;
      background: #fffaeb;
      color: #7a2e0e;
    }
    .hint {
      margin: 0.5rem 0 0;
      font-size: 0.8125rem;
    }
    .danger-zone {
      border-color: #fecdca;
    }
    .confirm {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      flex-wrap: wrap;
      padding: 0.75rem 1rem;
      border: 1px solid #fecdca;
      border-radius: 4px;
      background: #fef3f2;
    }
    .confirm p {
      margin: 0;
      flex: 1;
      min-width: 16rem;
    }
    .claims-head {
      display: flex;
      align-items: flex-end;
      justify-content: space-between;
      gap: 1rem;
      flex-wrap: wrap;
    }
    .claims-tools {
      display: flex;
      align-items: flex-end;
      gap: 0.5rem;
    }
    .filter {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
      color: #3f3f46;
    }
    .claim-form {
      border: 1px solid #e4e4e7;
      border-radius: 6px;
      padding: 0.875rem 1rem 1rem;
      margin-bottom: 1.25rem;
      background: #fafafa;
    }
    .claim-form h3 {
      margin: 0 0 0.5rem;
      font-size: 0.9375rem;
    }
    .actions-heading {
      text-align: right;
    }
    .actions {
      text-align: right;
      white-space: nowrap;
    }
    button.link {
      border: none;
      background: none;
      cursor: pointer;
      font-size: 0.75rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: #71717a;
    }
    button.link:hover {
      background: none;
      text-decoration: underline;
    }
    .empty {
      text-align: center;
      padding: 1.5rem;
    }
  `,
})
export class PolicyDetail {
  private readonly api = inject(PolicyManagerApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Bound from the `:id` route segment by `withComponentInputBinding()`, so it arrives as a string
   * even when the route is navigated to with a number. Converting here rather than in the template
   * keeps the API service taking the number it declares.
   */
  readonly id = input.required<string>();

  protected readonly statuses = POLICY_STATUSES;
  protected readonly claimStatuses = CLAIM_STATUSES;

  protected readonly claimState = new ListState();

  private readonly policyNumber = toId(this.id);

  protected readonly policyResource = injectResource(() => {
    const id = this.policyNumber();
    // A route that is not a positive integer never names a policy. Answering it as a 404 here
    // keeps the failure in the one place that already explains a missing policy, rather than
    // letting a NaN reach the API and come back as an opaque 400.
    return id === null
      ? throwError(() => new ApiError(404, null, 'That is not a policy identifier.'))
      : this.api.getPolicy(id);
  });

  protected readonly claimsResource = injectResource(() => {
    const id = this.policyNumber();
    return id === null
      ? throwError(() => new ApiError(404, null, 'That is not a policy identifier.'))
      : this.api.searchPolicyClaims(id, this.claimState.toQuery());
  });

  protected readonly policy = this.policyResource.data;
  protected readonly claims = this.claimsResource.data;

  /** A 404 means the policy is not there, which is a different answer from a failed request. */
  protected readonly missingPolicy = signal(false);

  protected premiumDraft = '';
  protected statusDraft: PolicyStatus = 'Active';
  protected readonly saving = signal(false);
  protected readonly editError = signal<ApiError | null>(null);
  protected readonly conflicted = signal(false);

  protected readonly confirmingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<ApiError | null>(null);

  protected readonly filingClaim = signal(false);
  protected readonly filing = signal(false);
  protected readonly claimError = signal<ApiError | null>(null);
  protected claimAmount = '';
  protected claimDescription = '';

  constructor() {
    // The edit form is prefilled from whatever loaded, and re-prefilled whenever a different policy
    // lands — a route change reuses this component, so the drafts have to follow the row rather
    // than keep the values typed for the previous one.
    effect(() => {
      const loaded = this.policyResource.data();
      if (loaded === null) {
        return;
      }
      this.premiumDraft = String(loaded.premium);
      this.statusDraft = loaded.status;
    });

    // A 404 is the only failure that changes what the page shows rather than what it reports, so it
    // is lifted out of the banner into its own message.
    effect(() => {
      const failure = this.policyResource.error();
      this.missingPolicy.set(failure !== null && failure.status === 404);
    });
  }

  protected canWrite(): boolean {
    return this.auth.hasRole('Admin', 'Adjuster');
  }

  /** Narrower than writing a policy: cancelling one is an `Admin`-only decision. */
  protected canDelete(): boolean {
    return this.auth.hasRole('Admin');
  }

  /**
   * Filing a claim is gated to `Admin` and `Adjuster` here even though the API's claims controller
   * leaves `POST /api/claims` open to any role. A claim is normally raised by the front line, so
   * hiding the control from an `Agent` is a choice about this page's scope, not a claim about what
   * the API permits — the endpoint would still accept a request sent another way.
   */
  protected canFileClaim(): boolean {
    return this.auth.hasRole('Admin', 'Adjuster');
  }

  /** What the form would change. An unchanged form sends nothing and is not submittable. */
  protected hasChanges(): boolean {
    const loaded = this.policyResource.data();
    if (loaded === null) {
      return false;
    }
    const premiumChanged =
      this.premiumDraft.trim() !== '' && Number(this.premiumDraft) !== loaded.premium;
    return premiumChanged || this.statusDraft !== loaded.status;
  }

  protected fieldError(field: string, source: ApiError | null = this.editError()): string | null {
    const messages = source?.errors[field];
    return messages && messages.length > 0 ? messages.join(' ') : null;
  }

  protected save(): void {
    const loaded = this.policyResource.data();
    if (loaded === null || this.saving() || !this.hasChanges()) {
      return;
    }

    const premium = Number(this.premiumDraft);
    const changes: { premium?: number; status?: PolicyStatus; rowVersion?: string } = {
      // Only the fields that differ: the update DTO treats an omitted premium as "leave it alone",
      // and sending it unchanged would be indistinguishable from an intentional edit.
      ...(Number.isFinite(premium) && premium !== loaded.premium ? { premium } : {}),
      ...(this.statusDraft !== loaded.status ? { status: this.statusDraft } : {}),
      // The token makes the write conditional on nothing having changed since this row was read. A
      // policy with no token predates concurrency, so the write goes through unconditionally rather
      // than being blocked on a token the server never issued.
      ...(loaded.rowVersion === null ? {} : { rowVersion: loaded.rowVersion }),
    };

    this.saving.set(true);
    this.editError.set(null);
    this.conflicted.set(false);

    this.api
      .updatePolicy(loaded.id, changes)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saving.set(false);
          // Reloaded rather than patched locally: the update response carries no body, and the
          // refreshed row brings the new rowVersion with it.
          this.policyResource.reload();
        },
        error: (failure: ApiError) => {
          this.saving.set(false);
          this.conflicted.set(failure.status === 409);
          this.editError.set(failure);
        },
      });
  }

  protected cancelPolicy(): void {
    const loaded = this.policyResource.data();
    if (loaded === null || this.deleting()) {
      return;
    }
    this.deleting.set(true);
    this.deleteError.set(null);

    // The token is not sent: `PolicyManagerApi.deletePolicy` takes only the id, because a DELETE
    // carries no body and the API service was written before the token was threaded through. The
    // cancel therefore lands unconditionally, which is a limitation of the client wrapper rather
    // than a decision about the write.
    this.api
      .deletePolicy(loaded.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deleting.set(false);
          this.confirmingDelete.set(false);
          // The row is still there and now reads Cancelled, so the page reloads rather than
          // navigating away — the point of the action is to see the policy in its new state.
          this.policyResource.reload();
        },
        error: (failure: ApiError) => {
          this.deleting.set(false);
          this.deleteError.set(failure);
        },
      });
  }

  protected toggleClaimForm(): void {
    if (!this.canFileClaim()) {
      return;
    }
    const opening = !this.filingClaim();
    this.filingClaim.set(opening);
    this.claimError.set(null);
    if (opening) {
      this.claimAmount = '';
      this.claimDescription = '';
    }
  }

  protected claimIsValid(): boolean {
    const amount = Number(this.claimAmount);
    return (
      this.claimAmount.trim() !== '' &&
      Number.isFinite(amount) &&
      amount >= 0.01 &&
      amount <= 99999999.99 &&
      this.claimDescription.trim().length > 0
    );
  }

  protected fileClaim(): void {
    const loaded = this.policyResource.data();
    if (loaded === null || this.filing() || !this.claimIsValid()) {
      return;
    }
    this.filing.set(true);
    this.claimError.set(null);

    this.api
      .createClaim({
        policyId: loaded.id,
        amount: Number(this.claimAmount),
        description: this.claimDescription.trim(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.filing.set(false);
          this.filingClaim.set(false);
          // Straight to the new claim: it is now Pending, and adjudicating it is the next thing
          // that has to happen to it. The claims table is reloaded too, so coming back does not
          // show a pre-claim page.
          this.claimsResource.reload();
          void this.router.navigate(['/claims', created.id]);
        },
        error: (failure: ApiError) => {
          this.filing.set(false);
          this.claimError.set(failure);
        },
      });
  }

  protected setClaimStatus(status: string): void {
    this.claimState.setStatus(status === '' ? undefined : status);
    this.claimsResource.reload();
  }

  protected sortClaimsBy(field: string): void {
    this.claimState.sortByField(field);
    this.claimsResource.reload();
  }

  protected goToClaimPage(page: number): void {
    this.claimState.goToPage(page);
    this.claimsResource.reload();
  }

  protected setClaimPageSize(size: number): void {
    this.claimState.setPageSize(size);
    this.claimsResource.reload();
  }

  protected asDate(value: string): string {
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleDateString();
  }

  protected asDateTime(value: string): string {
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
  }

  /** Shown truncated: the full base64 token is wider than the column and nobody reads it. */
  protected shortToken(token: string | null): string {
    if (token === null) {
      return '—';
    }
    return token.length <= 16 ? token : `${token.slice(0, 8)}…${token.slice(-4)}`;
  }
}

/** The route parameter as a policy identifier, or null when it is not one. */
function toId(id: () => string): () => number | null {
  return () => {
    const parsed = Number(id());
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  };
}
