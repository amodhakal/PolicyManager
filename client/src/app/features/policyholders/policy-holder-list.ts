import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../core/auth.service';
import { ListState } from '../../core/list-state';
import { ApiError } from '../../core/problem-details';
import { PolicyManagerApi } from '../../core/policy-manager-api.service';
import { injectResource } from '../../core/resource';
import { ErrorBanner } from '../../shared/error-banner';
import { Pager } from '../../shared/pager';

/**
 * Every policyholder, one page at a time, with the writes the token's role allows.
 *
 * The email column is rendered exactly as the API sent it. A masked address is a decision the
 * server made about who may see it, so trimming, reformatting or trying to detect the mask here
 * would misreport what the caller is entitled to.
 *
 * Delete is confirmed in the row rather than in `window.confirm`: a browser dialog is a blocking
 * global that cannot be styled, cannot be asserted against, and a row-level confirmation keeps the
 * name of the holder next to the question "delete this?".
 */
@Component({
  selector: 'app-policy-holder-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, ErrorBanner, Pager],
  template: `
    <h1>Policyholders</h1>
    <p class="muted subtitle">
      A deleted holder is hidden from this list but keeps their policies, and can be restored from
      their own page.
    </p>

    <app-error-banner [error]="resource.error()" />

    <div class="toolbar">
      <button
        type="button"
        class="primary"
        (click)="toggleCreate()"
        [disabled]="!canWrite() || resource.loading()"
        [title]="canWrite() ? null : noWriteReason"
      >
        {{ creating() ? 'Cancel' : 'New policyholder' }}
      </button>
      @if (saving()) {
        <span class="muted">Saving…</span>
      }
    </div>

    @if (creating()) {
      <form class="card" (ngSubmit)="create()">
        <h2>New policyholder</h2>
        <app-error-banner [error]="writeError()" />
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
        <button type="submit" class="primary" [disabled]="!draftIsValid() || saving()">
          Create
        </button>
      </form>
    }

    @if (resource.loading() && !resource.data()) {
      <p class="muted">Loading…</p>
    }

    @if (resource.data(); as page) {
      <table>
        <caption class="visually-hidden">
          Policyholders on page
          {{
            page.page
          }}
        </caption>
        <thead>
          <tr>
            <th scope="col" [attr.aria-sort]="sortState('lastName')">
              <button type="button" class="link" (click)="state.sortByField('lastName')">
                Name
                <span aria-hidden="true">{{ arrow('lastName') }}</span>
              </button>
            </th>
            <th scope="col" [attr.aria-sort]="sortState('email')">
              <button type="button" class="link" (click)="state.sortByField('email')">
                Email
                <span aria-hidden="true">{{ arrow('email') }}</span>
              </button>
            </th>
            <th scope="col">Last updated</th>
            <th scope="col" class="actions-heading">Actions</th>
          </tr>
        </thead>
        <tbody>
          @for (holder of page.items; track holder.id) {
            <tr>
              <td>
                <a [routerLink]="['/policyholders', holder.id]"
                  >{{ holder.lastName }}, {{ holder.firstName }}</a
                >
              </td>
              <td>{{ holder.email }}</td>
              <td>
                @if (holder.updatedAt) {
                  <span class="numeric-text">{{ asDate(holder.updatedAt) }}</span>
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
                  @if (confirmingId() === holder.id) {
                    <span class="confirm">
                      <span class="muted">Delete?</span>
                      <button
                        type="button"
                        class="link danger"
                        (click)="confirmDelete(holder.id)"
                        [disabled]="busyId() === holder.id"
                      >
                        Confirm
                      </button>
                      <button type="button" class="link" (click)="cancelDelete()">Cancel</button>
                    </span>
                  } @else {
                    <button
                      type="button"
                      class="link danger"
                      (click)="askDelete(holder.id)"
                      [disabled]="busyId() !== null"
                    >
                      Delete
                    </button>
                  }
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
    .actions-heading,
    .actions {
      text-align: right;
    }
    .actions {
      white-space: nowrap;
    }
    .action-link {
      margin-right: 0.5rem;
    }
    .confirm {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
    }
    .numeric-text {
      font-variant-numeric: tabular-nums;
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
  protected readonly resource = injectResource(() =>
    this.api.listPolicyHolders(this.state.toQuery()),
  );

  protected readonly creating = signal(false);
  protected readonly saving = signal(false);
  protected readonly writeError = signal<ApiError | null>(null);
  protected readonly confirmingId = signal<number | null>(null);
  protected readonly busyId = signal<number | null>(null);
  protected draft = { firstName: '', lastName: '', email: '' };

  protected readonly noWriteReason =
    'Creating or deleting a policyholder needs the Admin or Adjuster role.';

  /** Creating or deleting a holder is `Admin` or `Adjuster`; reading is any recognised role. */
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

  protected toggleCreate(): void {
    this.creating.update((open) => !open);
    this.writeError.set(null);
  }

  protected create(): void {
    if (!this.draftIsValid() || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.writeError.set(null);

    this.api
      .createPolicyHolder({
        firstName: this.draft.firstName.trim(),
        lastName: this.draft.lastName.trim(),
        email: this.draft.email.trim(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        // Straight to the new holder: creating a record and then paging back to a list that now
        // contains it makes the user hunt for the row they just made.
        next: (created) => {
          this.saving.set(false);
          this.creating.set(false);
          this.draft = { firstName: '', lastName: '', email: '' };
          void this.router.navigate(['/policyholders', created.id]);
        },
        error: (failure: ApiError) => {
          this.saving.set(false);
          this.writeError.set(failure);
        },
      });
  }

  protected askDelete(id: number): void {
    this.confirmingId.set(id);
  }

  protected cancelDelete(): void {
    this.confirmingId.set(null);
  }

  protected confirmDelete(id: number): void {
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
          this.confirmingId.set(null);
          this.resource.reload();
        },
        error: (failure: ApiError) => {
          this.busyId.set(null);
          this.confirmingId.set(null);
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

  protected sortState(field: string): 'ascending' | 'descending' | null {
    if (this.state.sortBy() !== field) {
      return null;
    }
    return this.state.descending() ? 'descending' : 'ascending';
  }

  /** The arrow is decoration; the direction is already carried by `aria-sort` on the header. */
  protected arrow(field: string): string {
    if (this.state.sortBy() !== field) {
      return '';
    }
    return this.state.descending() ? '▼' : '▲';
  }

  protected asDate(value: string): string {
    return new Date(value).toLocaleString();
  }
}
