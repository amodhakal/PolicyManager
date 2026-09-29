import { DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { filter, switchMap } from 'rxjs';

import { AuthService } from '../../core/auth.service';
import { ListState } from '../../core/list-state';
import { CLAIM_STATUSES, MAX_PAGE_SIZE, PageQuery } from '../../core/models';
import { ApiError } from '../../core/problem-details';
import { PolicyManagerApi } from '../../core/policy-manager-api.service';
import { injectResource } from '../../core/resource';
import { Badge } from '../../shared/badge';
import { ErrorBanner } from '../../shared/error-banner';
import { Pager } from '../../shared/pager';

/**
 * Every claim in the book, one page at a time.
 *
 * The claim number leads, for the same reason the policy number does on the policies list: it is the
 * server-assigned sequential business number a person would read out over the phone, where the row
 * id is an implementation detail. Nothing here derives it.
 *
 * The one rule this page has to make visible before it is acted on is that adjudication is final. An
 * approved or denied claim can never be deleted — `DELETE` on one answers 409 — so the delete
 * control is not offered on those rows at all. Hiding it with a stated reason is better than letting
 * someone discover the rule from a red banner, and better still than hiding it silently, which reads
 * as a missing feature rather than a deliberate one.
 *
 * Amounts are rendered with thousands separators and two decimals but no currency symbol. The API
 * stores `decimal(10,2)` and never says which currency a claim is denominated in, so a symbol would
 * be a guess the UI could not keep honest.
 */
@Component({
  selector: 'app-claim-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, FormsModule, RouterLink, Badge, ErrorBanner, Pager],
  template: `
    <h1>Claims</h1>
    <p class="muted subtitle">
      Approving or denying a claim is final: neither can be re-opened or deleted afterwards. A claim
      awaiting adjudication can be deleted, and doing so releases the amount it was reserving on its
      policy.
    </p>

    <div class="toolbar">
      <label class="filter">
        Status
        <select [value]="state.status() ?? ''" (change)="setStatus($any($event.target).value)">
          <option value="">All</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ option }}</option>
          }
        </select>
      </label>

      <span class="spacer"></span>

      <button
        type="button"
        class="primary"
        (click)="toggleCreate()"
        [title]="
          canWrite()
            ? null
            : 'Filing a claim needs an Admin or Adjuster token; this one is not eligible'
        "
      >
        {{ creating() ? 'Close' : 'File a claim' }}
      </button>
    </div>

    <app-error-banner [error]="resource.error()" />
    <app-error-banner [error]="rowError()" />

    @if (creating()) {
      <form class="card" (ngSubmit)="create()">
        <h2>File a claim</h2>

        @if (createError(); as failure) {
          <app-error-banner [error]="failure" />
        }

        <div class="fields">
          <div class="field wide">
            <label for="new-policy">Policy</label>
            <select id="new-policy" name="policyId" [(ngModel)]="draft.policyId" required>
              <option [ngValue]="null">Choose a policy…</option>
              @for (policy of visiblePolicies(); track policy.id) {
                <option [ngValue]="policy.id">
                  {{ policy.policyNumber }} — {{ policy.policyholderName }} ({{ policy.type }},
                  {{ policy.status }})
                </option>
              }
            </select>
            @if (fieldError('PolicyId'); as message) {
              <p class="field-error" role="alert">{{ message }}</p>
            }
            <p class="hint" [class.warn]="policyPageTotal() > 0 && visiblePolicies().length === 0">
              @if (policyPageTotal() === 0 && !policies.loading()) {
                No policies to choose from.
              } @else if (policyPageTotal() > 0) {
                Showing {{ policyRangeLabel() }} of {{ policyPageTotal() }}.
              }
            </p>
            <div class="policy-controls">
              <label class="filter">
                Filter this page
                <input
                  type="search"
                  name="policyFilter"
                  [ngModel]="policyFilter()"
                  (ngModelChange)="policyFilter.set($event)"
                  placeholder="number, name or #id"
                />
              </label>
              <button type="button" (click)="stepPolicyPage(-1)" [disabled]="policyPage() <= 1">
                Previous policies
              </button>
              <button
                type="button"
                (click)="stepPolicyPage(1)"
                [disabled]="policyPage() >= policyPageCount()"
              >
                Next policies
              </button>
            </div>
            <p class="hint">
              Only an active policy accepts claims, and the amount is measured against the cover
              still free after the claims already on it.
            </p>
            @if (policies.error(); as failure) {
              <app-error-banner [error]="failure" />
            }
          </div>

          <div class="field">
            <label for="new-amount">Amount</label>
            <input
              id="new-amount"
              name="amount"
              type="number"
              step="0.01"
              min="0.01"
              max="99999999.99"
              inputmode="decimal"
              [(ngModel)]="draft.amount"
              required
            />
            @if (fieldError('Amount'); as message) {
              <p class="field-error" role="alert">{{ message }}</p>
            }
          </div>

          <div class="field wide">
            <label for="new-description">Description</label>
            <textarea
              id="new-description"
              name="description"
              rows="3"
              [(ngModel)]="draft.description"
              required
            ></textarea>
            @if (fieldError('Description'); as message) {
              <p class="field-error" role="alert">{{ message }}</p>
            }
          </div>
        </div>

        <button type="submit" class="primary" [disabled]="!draftIsValid() || saving()">
          {{ saving() ? 'Filing…' : 'File claim' }}
        </button>
      </form>
    }

    @if (resource.loading() && !resource.data()) {
      <p class="muted">Loading…</p>
    }

    @if (resource.data(); as page) {
      <table>
        <thead>
          <tr>
            <th scope="col">
              <button type="button" class="link" (click)="sortBy('claimNumber')">
                Claim number
                @if (state.sortBy() === 'claimNumber') {
                  <span aria-hidden="true">{{ state.descending() ? '▼' : '▲' }}</span>
                }
              </button>
            </th>
            <th scope="col">Status</th>
            <th scope="col" class="numeric">
              <button type="button" class="link" (click)="sortBy('amount')">
                Amount
                @if (state.sortBy() === 'amount') {
                  <span aria-hidden="true">{{ state.descending() ? '▼' : '▲' }}</span>
                }
              </button>
            </th>
            <th scope="col">Policy</th>
            <th scope="col">
              <button type="button" class="link" (click)="sortBy('filedAt')">
                Filed
                @if (state.sortBy() === 'filedAt') {
                  <span aria-hidden="true">{{ state.descending() ? '▼' : '▲' }}</span>
                }
              </button>
            </th>
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
              <td>
                <a [routerLink]="['/policies', claim.policyId]">#{{ claim.policyId }}</a>
              </td>
              <td>{{ asDate(claim.filedAt) }}</td>
              <td class="actions">
                <a [routerLink]="['/claims', claim.id]">View</a>
                @if (isAdjudicated(claim.status)) {
                  <!--
                    Said rather than merely omitted: a row with no delete button would read as a
                    missing feature, when the absence is the rule itself.
                  -->
                  <span
                    class="final"
                    title="This claim has been {{
                      claim.status.toLowerCase()
                    }}. Adjudication is final, and deleting a decided claim is refused by the API."
                  >
                    Final
                  </span>
                } @else if (canWrite()) {
                  @if (confirmingId() === claim.id) {
                    <span class="confirm">
                      <span class="muted">Delete {{ claim.claimNumber }}?</span>
                      <button
                        type="button"
                        class="danger"
                        (click)="deleteClaim(claim.id)"
                        [disabled]="deletingId() === claim.id"
                      >
                        {{ deletingId() === claim.id ? 'Deleting…' : 'Yes' }}
                      </button>
                      <button type="button" (click)="confirmingId.set(null)">No</button>
                    </span>
                  } @else {
                    <button type="button" class="danger" (click)="confirmingId.set(claim.id)">
                      Delete
                    </button>
                  }
                }
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="6" class="muted empty">
                @if (state.status()) {
                  No {{ state.status() }} claims on this page.
                } @else {
                  No claims on this page.
                }
              </td>
            </tr>
          }
        </tbody>
      </table>

      <app-pager
        [result]="page"
        (pageChange)="goToPage($event)"
        (pageSizeChange)="setPageSize($event)"
      />
    }
  `,
  styles: `
    .subtitle {
      margin-top: 0;
      margin-bottom: 1rem;
      max-width: 75ch;
    }
    .toolbar {
      display: flex;
      align-items: flex-end;
      gap: 0.75rem;
      flex-wrap: wrap;
      margin-bottom: 1rem;
    }
    .spacer {
      flex: 1;
    }
    .filter {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
      color: #3f3f46;
    }
    .fields {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr));
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
    .hint {
      margin: 0;
      font-size: 0.8125rem;
      color: #71717a;
    }
    .hint.warn {
      color: #b54708;
    }
    .policy-controls {
      display: flex;
      align-items: flex-end;
      gap: 0.5rem;
      flex-wrap: wrap;
      margin-top: 0.5rem;
    }
    .field-error {
      margin: 0;
      font-size: 0.8125rem;
      color: #b42318;
    }
    .actions-heading {
      text-align: right;
    }
    .actions {
      text-align: right;
      white-space: nowrap;
    }
    .actions > * {
      margin-left: 0.5rem;
    }
    .final {
      font-size: 0.75rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: #71717a;
      cursor: help;
    }
    .confirm {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      padding: 0.125rem 0.375rem;
      border: 1px solid #fecdca;
      border-radius: 4px;
      background: #fef3f2;
      font-size: 0.8125rem;
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
export class ClaimList {
  private readonly api = inject(PolicyManagerApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly statuses = CLAIM_STATUSES;

  protected readonly state = new ListState();
  protected readonly resource = injectResource(() => this.api.listClaims(this.state.toQuery()));

  protected readonly creating = signal(false);
  protected readonly saving = signal(false);
  protected readonly createError = signal<ApiError | null>(null);

  protected readonly confirmingId = signal<number | null>(null);
  protected readonly deletingId = signal<number | null>(null);
  protected readonly rowError = signal<ApiError | null>(null);

  protected readonly policyPage = signal(1);
  protected readonly policyFilter = signal('');

  /**
   * The policy picker pages rather than searching.
   *
   * `GET /api/policies` takes paging and sorting but no search term, so a text box could only ever
   * narrow the rows already fetched — presenting it as a search would make a policy on page 9 look
   * absent. The picker asks for the largest page the API allows (100) ordered by policy number, says
   * plainly how much of the book that is, and offers next/previous. The filter box is scoped to the
   * loaded page and labelled as such, so it is a convenience and not a claim of completeness.
   *
   * The query is derived from `creating()` so nothing is requested until the form is actually open.
   */
  private readonly policyQuery = toObservable(
    computed<PageQuery | null>(() =>
      this.creating()
        ? { page: this.policyPage(), pageSize: MAX_PAGE_SIZE, sortBy: 'policyNumber' }
        : null,
    ),
  );

  protected readonly policies = injectResource(() =>
    this.policyQuery.pipe(
      filter((query) => query !== null),
      switchMap((query) => this.api.listPolicies(query)),
    ),
  );

  protected readonly policyPageTotal = computed(() => this.policies.data()?.totalCount ?? 0);
  protected readonly policyPageCount = computed(() => this.policies.data()?.totalPages ?? 1);

  protected readonly policyRangeLabel = computed(() => {
    const page = this.policies.data();
    if (page === null) {
      return '';
    }
    const first = (page.page - 1) * page.pageSize + 1;
    const last = first + page.items.length - 1;
    return `${first}–${Math.max(first, last)}`;
  });

  /** The loaded page narrowed by the filter box. Never a substitute for the paging controls. */
  protected readonly visiblePolicies = computed(() => {
    const needle = this.policyFilter().trim().toLowerCase();
    const items = this.policies.data()?.items ?? [];
    if (needle === '') {
      return items;
    }
    return items.filter((policy) =>
      `${policy.policyNumber} ${policy.policyholderName} ${policy.id}`
        .toLowerCase()
        .includes(needle),
    );
  });

  protected draft: ClaimDraft = emptyDraft();

  /** Filing a claim and deleting one are both `Admin` or `Adjuster`; reading the book is any role. */
  protected canWrite(): boolean {
    return this.auth.hasRole('Admin', 'Adjuster');
  }

  /**
   * Approved and Denied are terminal. The API permits the transition out of `Pending` only, so
   * anything else is already decided and the row must not offer the control the API would refuse.
   */
  protected isAdjudicated(status: string): boolean {
    return status !== 'Pending';
  }

  protected toggleCreate(): void {
    if (!this.canWrite()) {
      return;
    }
    const opening = !this.creating();
    this.creating.set(opening);
    this.createError.set(null);
    if (opening) {
      this.draft = emptyDraft();
    }
  }

  protected draftIsValid(): boolean {
    const { policyId, amount, description } = this.draft;
    return typeof policyId === 'number' && isAmount(amount) && description.trim().length > 0;
  }

  /** Field-level messages from a 400, keyed the way the API names the DTO properties. */
  protected fieldError(field: string): string | null {
    const messages = this.createError()?.errors[field];
    return messages && messages.length > 0 ? messages.join(' ') : null;
  }

  protected create(): void {
    if (this.saving() || !this.draftIsValid()) {
      return;
    }
    const { policyId, amount, description } = this.draft;
    if (typeof policyId !== 'number') {
      return;
    }

    this.saving.set(true);
    this.createError.set(null);

    this.api
      .createClaim({ policyId, amount: Number(amount), description: description.trim() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.creating.set(false);
          // Straight to the new claim: it lands Pending, and adjudicating it is the next thing that
          // has to happen to it. That is where the one-way decision is made, so that is where the
          // user is taken rather than back to a list they have to re-read.
          void this.router.navigate(['/claims', created.id]);
        },
        error: (failure: ApiError) => {
          this.saving.set(false);
          this.createError.set(failure);
        },
      });
  }

  /**
   * A soft delete: the row is hidden and the amount it was reserving on the policy is released, but
   * the claim itself survives and can be restored from its own page.
   */
  protected deleteClaim(id: number): void {
    if (this.deletingId() !== null) {
      return;
    }
    this.deletingId.set(id);
    this.rowError.set(null);

    this.api
      .deleteClaim(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deletingId.set(null);
          this.confirmingId.set(null);
          // The list is reloaded rather than navigated from: the deleted row should simply be
          // absent, and the page numbers stay meaningful.
          this.resource.reload();
        },
        error: (failure: ApiError) => {
          this.deletingId.set(null);
          this.confirmingId.set(null);
          this.rowError.set(failure);
        },
      });
  }

  protected setStatus(status: string): void {
    this.state.setStatus(status === '' ? undefined : status);
    this.resource.reload();
  }

  protected sortBy(field: string): void {
    this.state.sortByField(field);
    this.resource.reload();
  }

  protected goToPage(page: number): void {
    this.state.goToPage(page);
    this.resource.reload();
  }

  protected setPageSize(size: number): void {
    this.state.setPageSize(size);
    this.resource.reload();
  }

  protected stepPolicyPage(delta: number): void {
    this.policyPage.update((page) => Math.min(Math.max(1, page + delta), this.policyPageCount()));
  }

  protected asDate(value: string): string {
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleDateString();
  }
}

interface ClaimDraft {
  policyId: number | null;
  amount: string;
  description: string;
}

function emptyDraft(): ClaimDraft {
  return {
    policyId: null,
    amount: '',
    description: '',
  };
}

/**
 * A bound the client checks before posting, mirroring the DTO's range so the obvious typo is caught
 * without a round trip. The API still validates; this is about the message, not the guarantee.
 */
function isAmount(value: string): boolean {
  const parsed = Number(value);
  return value.trim() !== '' && Number.isFinite(parsed) && parsed >= 0.01 && parsed <= 99999999.99;
}
