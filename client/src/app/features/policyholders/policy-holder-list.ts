import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../core/auth.service';
import { ListState } from '../../core/list-state';
import { ApiError } from '../../core/problem-details';
import { PolicyManagerApi } from '../../core/policy-manager-api.service';
import { injectResource } from '../../core/resource';
import { Badge } from '../../shared/badge';
import { ErrorBanner } from '../../shared/error-banner';
import { Pager } from '../../shared/pager';

/**
 * Every policyholder, one page at a time, with the writes the token's role allows.
 *
 * The email column is rendered exactly as the API sent it. A masked address is a deliberate answer
 * from the server rather than a display choice, so reformatting or trimming it here would misreport
 * what the caller is entitled to see.
 */
@Component({
  selector: 'app-policy-holder-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Badge, ErrorBanner, Pager],
  template: `
    <h1>Policyholders</h1>
    <p class="muted subtitle">
      Soft-deleted holders are hidden from this list. Their policies stay in the book.
    </p>

    <app-error-banner [error]="resource.error()" />

    <div class="toolbar">
      <button
        type="button"
        class="primary"
        (click)="creating.set(!creating())"
        [disabled]="!canWrite()"
        [title]="canWrite() ? null : 'Your token does not carry a role that may create a holder'"
      >
        {{ creating() ? 'Cancel' : 'New policyholder' }}
      </button>
      @if (saving()) {
        <span class="muted">Saving…</span>
      }
    </div>

    @if (createError(); as failure) {
      <app-error-banner [error]="failure" />
    }

    @if (creating()) {
      <form class="card" (ngSubmit)="create()">
        <h2>New policyholder</h2>
        <div class="fields">
          <label>
            First name
            <input name="firstName" [(ngModel)]="draft.firstName" required maxlength="100" />
          </label>
          <label>
            Last name
            <input name="lastName" [(ngModel)]="draft.lastName" required maxlength="100" />
          </label>
          <label>
            Email
            <input name="email" type="email" [(ngModel)]="draft.email" required maxlength="254" />
          </label>
        </div>
        <button type="submit" class="primary" [disabled]="!draftIsValid() || saving()">Create</button>
      </form>
    }

    @if (resource.loading() && !resource.data()) {
      <p class="muted">Loading…</p>
    }

    @if (resource.data(); as page) {
      <table>
        <thead>
          <tr>
            <th>
              <button type="button" class="link" (click)="state.sortByField('lastName')">
                Name @if (state.sortBy() === 'lastName') {
                  <span aria-hidden="true">{{ state.descending() ? '↓' : '↑' }}</span>
                }
              </button>
            </th>
            <th>
              <button type="button" class="link" (click)="state.sortByField('email')">
                Email @if (state.sortBy() === 'email') {
                  <span aria-hidden="true">{{ state.descending() ? '↓' : '↑' }}</span>
                }
              </button>
            </th>
            <th>Last updated</th>
            <th class="actions-heading">Actions</th>
          </tr>
        </thead>
        <tbody>
          @for (holder of page.items; track holder.id) {
            <tr>
              <td>
                <a [routerLink]="['/policyholders', holder.id]">{{ holder.lastName }}, {{ holder.firstName }}</a>
              </td>
              <td>{{ holder.email }}</td>
              <td>
                @if (holder.updatedAt) {
                  {{ asDate(holder.updatedAt) }}
                  @if (holder.updatedBy) {
                    <span class="muted">by {{ holder.updatedBy }}</span>
                  }
                } @else {
                  <span class="muted">never</span>
                }
              </td>
              <td class="actions">
                <a class="action-link" [routerLink]="['/policyholders', holder.id]">View</a>
                @if (canWrite()) {
                  <button
                    type="button"
                    class="link danger"
                    (click)="remove(holder.id, holder.lastName)"
                    [disabled]="busyId() === holder.id"
                  >
                    Delete
                  </button>
                }
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="4" class="muted empty">No policyholders on this page.</td>
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
      align-items: center;
      gap: 0.75rem;
      margin-bottom: 1rem;
    }
    .fields {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: 0.75rem;
      margin-bottom: 1rem;
    }
    .fields label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .actions-heading {
      text-align: right;
    }
    .actions {
      text-align: right;
      white-space: nowrap;
    }
    .action-link {
      margin-right: 0.5rem;
    }
    button.link {
      border: none;
      background: none;
      cursor: pointer;
    }
    .empty {
      text-align: center;
      padding: 1.5rem;
    }
  `,
})
export class PolicyHolderList {
  private readonly api = inject(PolicyManagerApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly state = new ListState();
  protected readonly resource = injectResource(() => this.api.listPolicyHolders(this.state.toQuery()));

  protected readonly creating = signal(false);
  protected readonly saving = signal(false);
  protected readonly createError = signal<ApiError | null>(null);
  protected readonly busyId = signal<number | null>(null);
  protected draft = { firstName: '', lastName: '', email: '' };

  /** Creating or deleting a holder is `Admin` or `Adjuster`; reading is any role. */
  protected canWrite(): boolean {
    return this.auth.hasRole('Admin', 'Adjuster');
  }

  protected draftIsValid(): boolean {
    return (
      this.draft.firstName.trim().length > 0 &&
      this.draft.lastName.trim().length > 0 &&
      this.draft.email.trim().length > 0
    );
  }

  protected create(): void {
    if (!this.draftIsValid() || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.createError.set(null);

    this.api
      .createPolicyHolder({
        firstName: this.draft.firstName.trim(),
        lastName: this.draft.lastName.trim(),
        email: this.draft.email.trim(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.creating.set(false);
          this.draft = { firstName: '', lastName: '', email: '' };
          void this.router.navigate(['/policyholders', created.id]);
        },
        error: (failure: ApiError) => {
          this.saving.set(false);
          this.createError.set(failure);
        },
      });
  }

  protected remove(id: number, lastName: string): void {
    if (this.busyId() !== null) {
      return;
    }
    this.busyId.set(id);
    this.api
      .deletePolicyHolder(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busyId.set(null);
          this.resource.reload();
        },
        error: (failure: ApiError) => {
          this.busyId.set(null);
          this.resource.error.set(failure);
        },
      });
  }

  protected goToPage(page: number): void {
    this.state.goToPage(page);
    this.resource.reload();
  }

  protected setPageSize(size: number): void {
    this.state.setPageSize(size);
    this.resource.reload();
  }

  protected asDate(value: string): string {
    return new Date(value).toLocaleString();
  }
}
