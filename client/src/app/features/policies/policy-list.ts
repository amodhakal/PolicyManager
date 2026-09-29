import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { filter, switchMap } from 'rxjs';

import { AuthService } from '../../core/auth.service';
import { ListState } from '../../core/list-state';
import {
  MAX_PAGE_SIZE,
  POLICY_STATUSES,
  POLICY_TYPES,
  PageQuery,
  PolicyType,
} from '../../core/models';
import { ApiError } from '../../core/problem-details';
import { PolicyManagerApi } from '../../core/policy-manager-api.service';
import { injectResource } from '../../core/resource';
import { Badge } from '../../shared/badge';
import { ErrorBanner } from '../../shared/error-banner';
import { Pager } from '../../shared/pager';

/**
 * Every policy in the book, one page at a time.
 *
 * The policy number is the column that leads and the one every other column is subordinate to: it
 * is the sequential human-readable identifier an adjuster would read out over the phone, where the
 * row id is an implementation detail. Nothing here derives it.
 *
 * Amounts are rendered with thousands separators and two decimals but no currency symbol. The API
 * stores `decimal(10,2)` and never says which currency a premium is denominated in, so a symbol
 * would be a guess the UI could not keep honest.
 */
@Component({
  selector: 'app-policy-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, FormsModule, RouterLink, Badge, ErrorBanner, Pager],
  template: `
    <h1>Policies</h1>
    <p class="muted subtitle">
      Deleting a policy sets its status to Cancelled. The row stays, and there is no way to put it
      back.
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
            : 'Creating a policy needs an Admin or Adjuster token; this one is not eligible'
        "
      >
        {{ creating() ? 'Close' : 'New policy' }}
      </button>
    </div>

    <app-error-banner [error]="resource.error()" />

    @if (creating()) {
      <form class="card" (ngSubmit)="create()">
        <h2>New policy</h2>

        @if (createError(); as failure) {
          <app-error-banner [error]="failure" />
        }

        <div class="fields">
          <div class="field wide">
            <label for="new-holder">Policyholder</label>
            <select
              id="new-holder"
              name="policyHolderId"
              [(ngModel)]="draft.policyHolderId"
              required
            >
              <option [ngValue]="null">Choose a policyholder…</option>
              @for (holder of visibleHolders(); track holder.id) {
                <option [ngValue]="holder.id">
                  {{ holder.lastName }}, {{ holder.firstName }} (#{{ holder.id }})
                </option>
              }
            </select>
            @if (fieldError('PolicyHolderId'); as message) {
              <p class="field-error" role="alert">{{ message }}</p>
            }
            <p class="hint" [class.warn]="holderPageTotal() > 0 && visibleHolders().length === 0">
              @if (holderPageTotal() === 0 && !holders.loading()) {
                No policyholders to choose from.
              } @else if (holderPageTotal() > 0) {
                Showing {{ holderRangeLabel() }} of {{ holderPageTotal() }}.
              }
            </p>
            <div class="holder-controls">
              <label class="filter">
                Filter this page
                <input
                  type="search"
                  name="holderFilter"
                  [ngModel]="holderFilter()"
                  (ngModelChange)="holderFilter.set($event)"
                  placeholder="name or #id"
                />
              </label>
              <button type="button" (click)="stepHolderPage(-1)" [disabled]="holderPage() <= 1">
                Previous holders
              </button>
              <button
                type="button"
                (click)="stepHolderPage(1)"
                [disabled]="holderPage() >= holderPageCount()"
              >
                Next holders
              </button>
            </div>
            @if (holders.error(); as failure) {
              <app-error-banner [error]="failure" />
            }
          </div>

          <div class="field">
            <label for="new-type">Type</label>
            <select id="new-type" name="type" [(ngModel)]="draft.type" required>
              @for (option of types; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>
            @if (fieldError('Type'); as message) {
              <p class="field-error" role="alert">{{ message }}</p>
            }
          </div>

          <div class="field">
            <label for="new-premium">Premium</label>
            <input
              id="new-premium"
              name="premium"
              type="number"
              step="0.01"
              min="0.01"
              max="99999999.99"
              inputmode="decimal"
              [(ngModel)]="draft.premium"
              required
            />
            @if (fieldError('Premium'); as message) {
              <p class="field-error" role="alert">{{ message }}</p>
            }
          </div>

          <div class="field">
            <label for="new-limit">Coverage limit (optional)</label>
            <input
              id="new-limit"
              name="coverageLimit"
              type="number"
              step="0.01"
              min="0.01"
              max="99999999.99"
              inputmode="decimal"
              [(ngModel)]="draft.coverageLimit"
            />
            <p class="hint">Blank means no stated limit.</p>
            @if (fieldError('CoverageLimit'); as message) {
              <p class="field-error" role="alert">{{ message }}</p>
            }
          </div>

          <div class="field">
            <label for="new-start">Start date</label>
            <input
              id="new-start"
              name="startDate"
              type="date"
              [(ngModel)]="draft.startDate"
              required
            />
          </div>

          <div class="field">
            <label for="new-end">End date</label>
            <input id="new-end" name="endDate" type="date" [(ngModel)]="draft.endDate" required />
          </div>
        </div>

        @if (datesAreOutOfOrder()) {
          <p class="field-error" role="alert">
            The end date has to be later than the start date. The API rejects it otherwise, so it is
            caught here rather than round-tripped.
          </p>
        }

        <button type="submit" class="primary" [disabled]="!draftIsValid() || saving()">
          {{ saving() ? 'Creating…' : 'Create policy' }}
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
              <button type="button" class="link" (click)="sortBy('policyNumber')">
                Policy number
                @if (state.sortBy() === 'policyNumber') {
                  <span aria-hidden="true">{{ state.descending() ? '▼' : '▲' }}</span>
                }
              </button>
            </th>
            <th scope="col">Type</th>
            <th scope="col">Status</th>
            <th scope="col">Policyholder</th>
            <th scope="col" class="numeric">
              <button type="button" class="link" (click)="sortBy('premium')">
                Premium
                @if (state.sortBy() === 'premium') {
                  <span aria-hidden="true">{{ state.descending() ? '▼' : '▲' }}</span>
                }
              </button>
            </th>
            <th scope="col">
              <button type="button" class="link" (click)="sortBy('startDate')">
                Cover
                @if (state.sortBy() === 'startDate') {
                  <span aria-hidden="true">{{ state.descending() ? '▼' : '▲' }}</span>
                }
              </button>
            </th>
            <th scope="col" class="actions-heading">Actions</th>
          </tr>
        </thead>
        <tbody>
          @for (policy of page.items; track policy.id) {
            <tr>
              <td>
                <a [routerLink]="['/policies', policy.id]">{{ policy.policyNumber }}</a>
              </td>
              <td><app-badge [value]="policy.type" /></td>
              <td><app-badge [value]="policy.status" /></td>
              <td>
                <a [routerLink]="['/policyholders', policy.policyHolderId]">
                  {{ policy.policyholderName }}
                </a>
              </td>
              <td class="numeric">{{ policy.premium | number: '1.2-2' }}</td>
              <td class="dates">
                <span>{{ asDate(policy.startDate) }}</span>
                <span aria-hidden="true">–</span>
                <span>{{ asDate(policy.endDate) }}</span>
              </td>
              <td class="actions">
                <a [routerLink]="['/policies', policy.id]">View</a>
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="7" class="muted empty">
                @if (state.status()) {
                  No {{ state.status() }} policies on this page.
                } @else {
                  No policies on this page.
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
    .holder-controls {
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
    .dates {
      white-space: nowrap;
      font-variant-numeric: tabular-nums;
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
export class PolicyList {
  private readonly api = inject(PolicyManagerApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly statuses = POLICY_STATUSES;
  protected readonly types = POLICY_TYPES;

  protected readonly state = new ListState();
  protected readonly resource = injectResource(() => this.api.listPolicies(this.state.toQuery()));

  protected readonly creating = signal(false);
  protected readonly saving = signal(false);
  protected readonly createError = signal<ApiError | null>(null);

  protected readonly holderPage = signal(1);
  protected readonly holderFilter = signal('');

  /**
   * The policyholder picker pages rather than searching.
   *
   * `GET /api/policyholders` takes paging and sorting but no name filter, so a text box could only
   * ever narrow the rows already fetched — presenting it as a search would make holder 900 look
   * absent when they are simply on page 9. Instead the picker asks for the largest page the API
   * allows (100) ordered by last name, says plainly how much of the book that is, and offers
   * next/previous. The filter box is scoped to the loaded page and labelled as such, so it is a
   * convenience and not a claim of completeness.
   *
   * The query is derived from `creating()` so nothing is requested until the form is actually open.
   */
  private readonly holderQuery = toObservable(
    computed<PageQuery | null>(() =>
      this.creating()
        ? { page: this.holderPage(), pageSize: MAX_PAGE_SIZE, sortBy: 'lastName' }
        : null,
    ),
  );

  protected readonly holders = injectResource(() =>
    this.holderQuery.pipe(
      filter((query) => query !== null),
      switchMap((query) => this.api.listPolicyHolders(query)),
    ),
  );

  protected readonly holderPageTotal = computed(() => this.holders.data()?.totalCount ?? 0);
  protected readonly holderPageCount = computed(() => this.holders.data()?.totalPages ?? 1);

  protected readonly holderRangeLabel = computed(() => {
    const page = this.holders.data();
    if (page === null) {
      return '';
    }
    const first = (page.page - 1) * page.pageSize + 1;
    const last = first + page.items.length - 1;
    return `${first}–${Math.max(first, last)}`;
  });

  /** The loaded page narrowed by the filter box. Never a substitute for the paging controls. */
  protected readonly visibleHolders = computed(() => {
    const needle = this.holderFilter().trim().toLowerCase();
    const items = this.holders.data()?.items ?? [];
    if (needle === '') {
      return items;
    }
    return items.filter((holder) =>
      `${holder.lastName} ${holder.firstName} ${holder.id}`.toLowerCase().includes(needle),
    );
  });

  protected draft: PolicyDraft = emptyDraft();

  /** Issuing a policy is `Admin` or `Adjuster`; reading the book is any role. */
  protected canWrite(): boolean {
    return this.auth.hasRole('Admin', 'Adjuster');
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

  /**
   * The API binds the dates as `DateTime`, and a date input has no time component, so `YYYY-MM-DD`
   * is sent verbatim rather than being converted through a local `Date` first — converting would
   * shift the value by a day either side of a timezone change.
   */
  protected datesAreOutOfOrder(): boolean {
    const { startDate, endDate } = this.draft;
    return startDate !== '' && endDate !== '' && endDate <= startDate;
  }

  protected draftIsValid(): boolean {
    const { policyHolderId, type, premium, startDate, endDate } = this.draft;
    return (
      typeof policyHolderId === 'number' &&
      this.types.includes(type) &&
      isAmount(premium) &&
      (this.draft.coverageLimit.trim() === '' || isAmount(this.draft.coverageLimit)) &&
      startDate !== '' &&
      endDate !== '' &&
      !this.datesAreOutOfOrder()
    );
  }

  /** Field-level messages from a 400, keyed the way the API names the DTO properties. */
  protected fieldError(field: string): string | null {
    const messages = this.createError()?.errors[field];
    return messages && messages.length > 0 ? messages.join(' ') : null;
  }

  protected create(): void {
    if (!this.draftIsValid() || this.saving()) {
      return;
    }
    const { policyHolderId, type, premium, coverageLimit, startDate, endDate } = this.draft;
    if (typeof policyHolderId !== 'number' || !this.types.includes(type)) {
      return;
    }

    this.saving.set(true);
    this.createError.set(null);

    this.api
      .createPolicy({
        premium: Number(premium),
        policyHolderId,
        type,
        // Omitted rather than sent as null: an absent limit means no stated limit, and the same
        // rule that made the update DTO nullable applies here.
        coverageLimit: coverageLimit.trim() === '' ? undefined : Number(coverageLimit),
        startDate,
        endDate,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.creating.set(false);
          // The server assigned the policy number, so the new policy's own page is where that
          // number is worth showing; navigating away from a list that has not refetched avoids
          // showing a stale book underneath it.
          void this.router.navigate(['/policies', created.id]);
        },
        error: (failure: ApiError) => {
          this.saving.set(false);
          this.createError.set(failure);
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

  protected stepHolderPage(delta: number): void {
    this.holderPage.update((page) => Math.min(Math.max(1, page + delta), this.holderPageCount()));
  }

  protected asDate(value: string): string {
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleDateString();
  }
}

interface PolicyDraft {
  policyHolderId: number | null;
  type: PolicyType;
  premium: string;
  coverageLimit: string;
  startDate: string;
  endDate: string;
}

function emptyDraft(): PolicyDraft {
  return {
    policyHolderId: null,
    type: 'Auto',
    premium: '',
    coverageLimit: '',
    startDate: '',
    endDate: '',
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
