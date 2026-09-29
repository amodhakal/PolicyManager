import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ListState } from '../../core/list-state';
import { PolicyManagerApi } from '../../core/policy-manager-api.service';
import { injectResource } from '../../core/resource';
import { Badge } from '../../shared/badge';
import { ErrorBanner } from '../../shared/error-banner';
import { Pager } from '../../shared/pager';

/**
 * The three read-only roll-ups the API offers, on one page.
 *
 * They are three separate `Resource` instances rather than one combined fetch. The endpoints are
 * independent queries and they fail independently — a claims roll-up that times out says nothing
 * about the premium one — so a single combined resource would let one failure blank the other two
 * reports, and the page would lose most of its content to an error about a fraction of it.
 *
 * None of the three endpoints takes a date range, and none of them is built to take one, so there
 * is no date control here. A filter that the API ignores is worse than no filter: the numbers would
 * look filtered and would not be.
 *
 * The two grouped tables render every row the API sends, including the ones at zero. The endpoints
 * emit a row per enum member whether or not anything matches, which is deliberate: the shape of the
 * report does not change as data arrives, so a reader learns that `Denied` exists and is currently
 * empty rather than inferring it from its absence.
 *
 * Amounts render with thousands separators and two decimals but no currency symbol, for the same
 * reason the policy list does it that way: the API stores `decimal(10,2)` and never says which
 * currency a premium or a claim amount is denominated in.
 */
@Component({
  selector: 'app-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, RouterLink, Badge, ErrorBanner, Pager],
  template: `
    <h1>Reports</h1>
    <p class="muted subtitle">
      Three roll-ups over the current book: open claims by status, premium by policy type, and open
      claims per policyholder. Soft-deleted claims are excluded; soft-deleted holders' policies are
      not. All figures are live totals — there is no date range on any of them.
    </p>

    <div class="toolbar">
      <button type="button" (click)="refresh()">Refresh</button>
    </div>

    <!-- ── Report 1 ──────────────────────────────────────────────────────────── -->
    <section class="card" aria-labelledby="open-claims-heading">
      <h2 id="open-claims-heading">Open claims by status</h2>
      <p class="muted lede">
        Open means not yet decided: a denied claim is the one status that is not open, so a status
        can appear here at zero and still be reported.
      </p>

      <div class="error-slot" role="alert">
        <app-error-banner [error]="openClaims.error()" />
      </div>

      @if (openClaims.loading() && !openClaims.data()) {
        <p class="muted">Loading…</p>
      }

      @if (openClaims.data(); as report) {
        <div class="stats">
          <div class="stat">
            <span class="stat-label">Open claims</span>
            <span class="stat-value">{{ report.totalOpenClaims | number }}</span>
          </div>
          <div class="stat">
            <span class="stat-label">Open amount</span>
            <span class="stat-value numeric">{{ report.totalOpenAmount | number: '1.2-2' }}</span>
          </div>
        </div>

        <table>
          <caption class="visually-hidden">
            Open claims grouped by claim status, with counts and amounts per status
          </caption>
          <thead>
            <tr>
              <th scope="col">Status</th>
              <th scope="col" class="numeric">Claims</th>
              <th scope="col" class="numeric">Total amount</th>
              <th scope="col" class="numeric">Average amount</th>
            </tr>
          </thead>
          <tbody>
            @for (row of report.statuses; track row.status) {
              <tr>
                <td><app-badge [value]="row.status" /></td>
                <td class="numeric">{{ row.claimCount | number }}</td>
                <td class="numeric">{{ row.totalAmount | number: '1.2-2' }}</td>
                <td class="numeric">{{ row.averageAmount | number: '1.2-2' }}</td>
              </tr>
            } @empty {
              <tr>
                <td colspan="4" class="muted empty">
                  The report came back without any status rows.
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>

    <!-- ── Report 2 ──────────────────────────────────────────────────────────── -->
    <section class="card" aria-labelledby="premium-heading">
      <h2 id="premium-heading">Premium by policy type</h2>
      <p class="muted lede">Every policy in the book, grouped by the type it was written as.</p>

      <div class="error-slot" role="alert">
        <app-error-banner [error]="premium.error()" />
      </div>

      @if (premium.loading() && !premium.data()) {
        <p class="muted">Loading…</p>
      }

      @if (premium.data(); as report) {
        <div class="stats">
          <div class="stat">
            <span class="stat-label">Policies</span>
            <span class="stat-value">{{ report.totalPolicies | number }}</span>
          </div>
          <div class="stat">
            <span class="stat-label">Total premium</span>
            <span class="stat-value numeric">{{ report.totalPremium | number: '1.2-2' }}</span>
          </div>
        </div>

        <table>
          <caption class="visually-hidden">
            Policies grouped by policy type, with policy counts and premiums per type
          </caption>
          <thead>
            <tr>
              <th scope="col">Type</th>
              <th scope="col" class="numeric">Policies</th>
              <th scope="col" class="numeric">Total premium</th>
              <th scope="col" class="numeric">Average premium</th>
            </tr>
          </thead>
          <tbody>
            @for (row of report.types; track row.type) {
              <tr>
                <td><app-badge [value]="row.type" /></td>
                <td class="numeric">{{ row.policyCount | number }}</td>
                <td class="numeric">{{ row.totalPremium | number: '1.2-2' }}</td>
                <td class="numeric">{{ row.averagePremium | number: '1.2-2' }}</td>
              </tr>
            } @empty {
              <tr>
                <td colspan="4" class="muted empty">The report came back without any type rows.</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>

    <!-- ── Report 3 ──────────────────────────────────────────────────────────── -->
    <section class="card" aria-labelledby="ratio-heading">
      <h2 id="ratio-heading">Open claims per policyholder</h2>
      <p class="muted lede">
        The ratio is open claim amount over total premium for that holder's policies, so it reads as
        how much of the premium is currently claimed. Holders with no policy do not appear: with no
        premium there is no ratio, and a page of zeros would bury the holders the report is about.
      </p>

      <div class="error-slot" role="alert">
        <app-error-banner [error]="ratios.error()" />
      </div>

      <div class="toolbar">
        <button type="button" (click)="toggleRatioOrder()">
          {{ ratioDescending() ? 'Heaviest first' : 'Lightest first' }}
          <span aria-hidden="true">{{ ratioDescending() ? '▼' : '▲' }}</span>
        </button>
        <span class="muted order-note">{{ ratioOrderNote() }}</span>
      </div>

      @if (ratios.loading() && !ratios.data()) {
        <p class="muted">Loading…</p>
      }

      @if (ratios.data(); as page) {
        <table>
          <caption class="visually-hidden">
            Policyholders ordered by open claim amount over total premium, heaviest ratio first
          </caption>
          <thead>
            <tr>
              <th scope="col">Policyholder</th>
              <th scope="col" class="numeric">Policies</th>
              <th scope="col" class="numeric">Total premium</th>
              <th scope="col" class="numeric">Open claims</th>
              <th scope="col" class="numeric">Open claim amount</th>
              <th scope="col" class="numeric ratio-heading">Claims ratio</th>
            </tr>
          </thead>
          <tbody>
            @for (row of page.items; track row.policyHolderId) {
              <tr>
                <td>
                  <a [routerLink]="['/policyholders', row.policyHolderId]">
                    {{ row.policyholderName }}
                  </a>
                </td>
                <td class="numeric">{{ row.policyCount | number }}</td>
                <td class="numeric">{{ row.totalPremium | number: '1.2-2' }}</td>
                <td class="numeric">{{ row.openClaimCount | number }}</td>
                <td class="numeric">{{ row.totalOpenClaimAmount | number: '1.2-2' }}</td>
                <td class="numeric ratio">{{ row.claimsRatio | number: '1.0-4' }}</td>
              </tr>
            } @empty {
              <tr>
                <td colspan="6" class="muted empty">
                  No policyholder owns a policy yet, so there is no ratio to report.
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
    </section>
  `,
  styles: `
    .subtitle {
      margin-top: 0;
      margin-bottom: 1rem;
    }
    .lede {
      margin-top: 0;
      font-size: 0.875rem;
    }
    .toolbar {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      flex-wrap: wrap;
      margin-bottom: 1rem;
    }
    .stats {
      display: flex;
      flex-wrap: wrap;
      gap: 1.5rem;
      margin-bottom: 1rem;
    }
    .stat {
      display: flex;
      flex-direction: column;
      gap: 0.125rem;
    }
    .stat-label {
      font-size: 0.75rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: #71717a;
    }
    .stat-value {
      font-size: 1.25rem;
      font-weight: 600;
    }
    .order-note {
      font-size: 0.8125rem;
    }
    .ratio-heading {
      color: #175cd3;
    }
    td.ratio {
      font-weight: 600;
    }
    .empty {
      text-align: center;
      padding: 1.5rem;
    }
  `,
})
export class Reports {
  private readonly api = inject(PolicyManagerApi);

  /** Declared before the resources because each one's first request reads its query. */
  private readonly state = new ListState();

  /**
   * Three resources, named for the report each one carries, so a failure reads as "the premium
   * report failed" rather than "the page failed".
   */
  protected readonly openClaims = injectResource(() => this.api.openClaimsByStatus());
  protected readonly premium = injectResource(() => this.api.premiumByType());
  protected readonly ratios = injectResource(() =>
    this.api.claimsRatioPerHolder(this.state.toQuery()),
  );

  /**
   * Whether the ratio list is showing the heaviest holders first.
   *
   * This report's default sort is ratio descending, which is the whole point of it: the first page
   * is the holders to look at. The `ListState` starts with no sort field and `descending` false, and
   * that combination sends no sort parameters at all — which is exactly how the endpoint's own
   * default is selected. `sortByField()` cannot be used for the toggle here, because it would send
   * `descending=true` to reverse a default that is already descending, producing the *lightest*
   * holders when the label claims the heaviest.
   */
  protected readonly ratioDescending = computed(() => {
    if (this.state.sortBy() === 'claimsRatio') {
      return this.state.descending();
    }
    return true;
  });

  protected ratioOrderNote(): string {
    return this.state.sortBy() === 'claimsRatio'
      ? 'Sorted by claims ratio'
      : 'Default order: the API sorts by claims ratio, highest first';
  }

  protected toggleRatioOrder(): void {
    if (this.state.sortBy() === 'claimsRatio') {
      this.state.descending.update((current) => !current);
    } else {
      this.state.sortBy.set('claimsRatio');
      this.state.descending.set(true);
    }
    this.state.page.set(1);
    this.ratios.reload();
  }

  protected goToPage(page: number): void {
    this.state.goToPage(page);
    this.ratios.reload();
  }

  protected setPageSize(size: number): void {
    this.state.setPageSize(size);
    this.ratios.reload();
  }

  /** Re-issues all three. Independent calls, so one failing does not cancel the other two. */
  protected refresh(): void {
    this.openClaims.reload();
    this.premium.reload();
    this.ratios.reload();
  }
}
