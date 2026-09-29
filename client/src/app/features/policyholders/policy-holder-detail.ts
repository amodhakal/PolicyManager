import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../core/auth.service';
import { ListState } from '../../core/list-state';
import {
  PagedResult,
  Policy,
  PolicyHolder,
  PolicyStatus,
  UpdatePolicyHolder,
} from '../../core/models';
import { ApiError } from '../../core/problem-details';
import { PolicyManagerApi } from '../../core/policy-manager-api.service';
import { Resource } from '../../core/resource';
import { Badge } from '../../shared/badge';
import { ErrorBanner } from '../../shared/error-banner';
import { Pager } from '../../shared/pager';

/**
 * One policyholder: their details, the writes allowed on them, and the policies they own.
 *
 * The holder and the policies are two separate resources rather than one because they fail
 * separately and mean different things when they do. A 404 on the holder is a deleted or
 * mistyped id, and the policies query answers 404 too, so the two have to be reported as the same
 * "no such holder" rather than as a holder who owns nothing and a list that broke.
 *
 * Restore is deliberately reachable when the holder itself failed to load. The API's read filter
 * excludes soft-deleted rows, so a deleted holder's own detail page is the one place its id is
 * still known — a Restore hidden behind a successful GET would be the one control that can never
 * appear for the exact rows it exists to recover.
 */
@Component({
  selector: 'app-policy-holder-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, FormsModule, RouterLink, Badge, ErrorBanner, Pager],
  template: `
    <p class="back"><a routerLink="/policyholders">← All policyholders</a></p>

    @if (invalidId()) {
      <div class="card">
        <h1>Not a policyholder id</h1>
        <p class="muted" role="alert">
          “{{ id() }}” is not an id this API could have issued, so nothing was requested.
        </p>
      </div>
    } @else {
      @if (holderLoading() && !holder()) {
        <p class="muted">Loading…</p>
      }

      @if (holderError() && !notFound()) {
        <app-error-banner [error]="holderError()" />
      }

      @if (notFound()) {
        <div class="card missing">
          <h1>No such policyholder</h1>
          <p class="muted">
            The API answered 404 for this id. Either the id was mistyped, or the holder was deleted
            — a deleted holder is hidden from reads but keeps their policies, and restoring one
            brings them back exactly as they were.
          </p>
          @if (canRestore()) {
            <div class="actions">
              <button type="button" class="primary" (click)="restore()" [disabled]="restoring()">
                {{ restoring() ? 'Restoring…' : 'Restore this policyholder' }}
              </button>
            </div>
          }
          <app-error-banner [error]="writeError()" />
        </div>
      } @else if (holder(); as current) {
        <div class="card">
          <div class="heading">
            <h1>{{ current.lastName }}, {{ current.firstName }}</h1>
            <div class="actions">
              @if (canWrite()) {
                @if (confirmingDelete()) {
                  <span class="confirm">
                    <span class="muted">Delete this holder?</span>
                    <button type="button" class="danger" (click)="remove()" [disabled]="deleting()">
                      Confirm
                    </button>
                    <button type="button" (click)="confirmingDelete.set(false)">Cancel</button>
                  </span>
                } @else {
                  <button type="button" class="danger" (click)="confirmingDelete.set(true)">
                    Delete
                  </button>
                }
              }
            </div>
          </div>
          <app-error-banner [error]="writeError()" />

          <dl class="facts">
            <dt>Email</dt>
            <dd>{{ current.email }}</dd>
            <dt>Last updated</dt>
            <dd>
              @if (current.updatedAt) {
                {{ current.updatedAt | date: 'medium' }}
                @if (current.updatedBy) {
                  <span class="muted">by {{ current.updatedBy }}</span>
                }
              } @else {
                <span class="muted">never</span>
              }
            </dd>
            <dt>Row version</dt>
            <dd>
              <code class="token" [title]="current.rowVersion">{{
                shortToken(current.rowVersion)
              }}</code>
              <span class="muted hint">concurrency token, sent back on the next write</span>
            </dd>
          </dl>
        </div>

        @if (canWrite()) {
          <form class="card" (ngSubmit)="save()">
            <h2>Edit details</h2>
            <p class="muted note">
              Only the fields you change are sent. An omitted field is left as it is, while a field
              sent blank is rejected, so an untouched box can never blank out a stored value.
            </p>
            <app-error-banner [error]="saveError()" />
            <div class="fields">
              <label>
                First name
                <input
                  name="firstName"
                  required
                  maxlength="100"
                  [ngModel]="firstName()"
                  (ngModelChange)="firstName.set($event)"
                />
              </label>
              <label>
                Last name
                <input
                  name="lastName"
                  required
                  maxlength="100"
                  [ngModel]="lastName()"
                  (ngModelChange)="lastName.set($event)"
                />
              </label>
              <label>
                Email
                <input
                  name="email"
                  type="email"
                  required
                  maxlength="254"
                  [ngModel]="email()"
                  (ngModelChange)="email.set($event)"
                />
              </label>
            </div>
            @if (fieldErrors().length > 0) {
              <ul class="field-errors" role="alert">
                @for (entry of fieldErrors(); track entry.field) {
                  @for (message of entry.messages; track message) {
                    <li>
                      <strong>{{ entry.field }}</strong
                      >: {{ message }}
                    </li>
                  }
                }
              </ul>
            }
            <div class="actions">
              <button type="submit" class="primary" [disabled]="saving() || !isValid()">
                {{ saving() ? 'Saving…' : 'Save changes' }}
              </button>
              <span class="muted">{{ saveNote() }}</span>
            </div>
          </form>
        }
      }

      <section class="card">
        <div class="heading">
          <h2>Policies</h2>
          <label class="filter">
            <span>Status</span>
            <select [value]="state.status() ?? ''" (change)="onStatusChange($event)">
              <option value="">All</option>
              @for (option of statusOptions; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>
          </label>
        </div>

        <app-error-banner [error]="policiesError()" />

        @if (policiesLoading() && !policies()) {
          <p class="muted">Loading…</p>
        }

        @if (policies(); as page) {
          <table>
            <caption class="visually-hidden">
              Policies held by this policyholder
            </caption>
            <thead>
              <tr>
                <th scope="col">Policy number</th>
                <th scope="col">Type</th>
                <th scope="col">Status</th>
                <th scope="col" class="numeric">Premium</th>
                <th scope="col">Start</th>
                <th scope="col">End</th>
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
                  <td class="numeric">{{ premium(policy.premium) }}</td>
                  <td>{{ policy.startDate | date: 'medium' }}</td>
                  <td>{{ policy.endDate | date: 'medium' }}</td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="6" class="muted empty">{{ emptyPoliciesMessage() }}</td>
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
    }
  `,
  styles: `
    .back {
      margin: 0 0 1rem;
    }
    .heading {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      gap: 1rem;
      flex-wrap: wrap;
    }
    .actions {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      flex-wrap: wrap;
    }
    .confirm {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
    }
    .facts {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.375rem 1rem;
      margin: 1rem 0 0;
    }
    .facts dt {
      font-size: 0.75rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--text-muted);
    }
    .facts dd {
      margin: 0;
    }
    .token {
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
      font-size: 0.8125rem;
    }
    .hint {
      margin-left: 0.5rem;
      font-size: 0.8125rem;
    }
    .note {
      margin-top: 0;
      font-size: 0.875rem;
    }
    .fields {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: 0.75rem;
      margin-bottom: 0.75rem;
    }
    .fields label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .field-errors {
      margin: 0 0 0.75rem;
      padding-left: 1.125rem;
      color: #b42318;
      font-size: 0.875rem;
    }
    .filter {
      display: flex;
      align-items: center;
      gap: 0.375rem;
      color: var(--text-muted);
    }
    .empty {
      text-align: center;
      padding: 1.5rem;
    }
    .missing p {
      max-width: 44rem;
    }
  `,
})
export class PolicyHolderDetail {
  private readonly api = inject(PolicyManagerApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * The route param arrives as a string. Component input binding is configured
   * (`withComponentInputBinding()`), so the router sets this before the first change detection and
   * no `ActivatedRoute` subscription is needed.
   */
  readonly id = input.required<string>();

  protected readonly state = new ListState();
  protected readonly statusOptions: readonly PolicyStatus[] = ['Active', 'Cancelled', 'Expired'];

  private readonly holderId = signal<number | null>(null);
  private readonly invalidId = signal(false);
  private readonly holderResource = signal<Resource<PolicyHolder> | null>(null);
  private readonly policiesResource = signal<Resource<PagedResult<Policy>> | null>(null);

  protected readonly holder = computed(() => this.holderResource()?.data() ?? null);
  protected readonly holderError = computed(() => this.holderResource()?.error() ?? null);
  protected readonly holderLoading = computed(() => this.holderResource()?.loading() ?? false);

  protected readonly policies = computed(() => this.policiesResource()?.data() ?? null);
  protected readonly policiesError = computed(() => this.policiesResource()?.error() ?? null);
  protected readonly policiesLoading = computed(() => this.policiesResource()?.loading() ?? false);

  /**
   * A 404 on the holder is not a generic failure: the read filter hides soft-deleted rows, so this
   * is what a deleted holder looks like from here — the same answer as a mistyped id, which is why
   * the page offers the restore that distinguishes them.
   */
  protected readonly notFound = computed(() => this.holderError()?.status === 404);

  protected readonly firstName = signal('');
  protected readonly lastName = signal('');
  protected readonly email = signal('');
  protected readonly saving = signal(false);
  protected readonly saveError = signal<ApiError | null>(null);
  protected readonly saveNote = signal('');

  protected readonly writeError = signal<ApiError | null>(null);
  protected readonly confirmingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly restoring = signal(false);

  constructor() {
    // Built here rather than as field initialisers: the resources fetch immediately on
    // construction, and a required input has no value yet while the constructor runs. An effect
    // first executes during change detection, by which point the router has bound `id`.
    //
    // Moving between two holders reuses this component and only rebinds `id`, so a new pair of
    // resources replaces the old ones here. The old pair stays subscribed until the component is
    // destroyed, which costs one in-flight request at most and cannot repaint anything: nothing
    // reads a resource that is no longer the one held by the signal.
    effect(() => {
      const raw = this.id();
      const parsed = toHolderId(raw);
      untracked(() => {
        this.invalidId.set(parsed === null);
        this.holderId.set(parsed);
        this.holderResource.set(null);
        this.policiesResource.set(null);
        this.resetForms();
        if (parsed === null) {
          return;
        }
        this.state.reset();
        this.holderResource.set(
          new Resource(() => this.api.getPolicyHolder(parsed), this.destroyRef),
        );
        this.policiesResource.set(
          new Resource(
            () => this.api.searchHolderPolicies(parsed, this.state.toQuery()),
            this.destroyRef,
          ),
        );
      });
    });

    // Prefills the form from the loaded holder. A masked email lands in the box exactly as the
    // server sent it, which is harmless because an unchanged field is never sent back.
    effect(() => {
      const current = this.holder();
      if (current === null) {
        return;
      }
      untracked(() => {
        this.firstName.set(current.firstName);
        this.lastName.set(current.lastName);
        this.email.set(current.email);
        this.saveError.set(null);
        this.saveNote.set('');
      });
    });
  }

  protected canWrite(): boolean {
    return this.auth.hasRole('Admin', 'Adjuster');
  }

  /**
   * Offered whenever the holder could not be read, which is the only state Restore can act on — a
   * live holder is not deleted, and a restore of one would be refused.
   */
  protected canRestore(): boolean {
    return this.canWrite() && this.holderId() !== null && this.holder() === null;
  }

  protected isValid(): boolean {
    return (
      this.firstName().trim().length > 0 &&
      this.lastName().trim().length > 0 &&
      this.email().trim().length > 0
    );
  }

  protected fieldErrors(): { field: string; messages: string[] }[] {
    return Object.entries(this.saveError()?.errors ?? {}).map(([field, messages]) => ({
      field,
      messages,
    }));
  }

  protected save(): void {
    const current = this.holder();
    if (current === null || this.saving() || !this.isValid()) {
      return;
    }

    const update: UpdatePolicyHolder = {};
    if (this.firstName().trim() !== current.firstName) {
      update.firstName = this.firstName().trim();
    }
    if (this.lastName().trim() !== current.lastName) {
      update.lastName = this.lastName().trim();
    }
    if (this.email().trim() !== current.email) {
      update.email = this.email().trim();
    }

    // The API takes a partial body but rejects one where all three keys are absent, so an
    // unchanged form is not sent at all rather than sent as an empty object. A key left off the
    // object is omitted from the JSON, which the API reads as "leave this alone"; a key sent as
    // null or blank is a value, and it is refused.
    if (Object.keys(update).length === 0) {
      this.saveNote.set('Nothing has changed.');
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    this.saveNote.set('');

    this.api
      .updatePolicyHolder(current.id, update)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.saveNote.set('Saved.');
          // Written straight into the cache so the form is not repainted from a stale row while the
          // reload is still in flight.
          this.holderResource()?.data.set(saved);
          this.holderResource()?.reload();
        },
        error: (failure: ApiError) => {
          this.saving.set(false);
          this.saveError.set(failure);
        },
      });
  }

  protected remove(): void {
    const id = this.holderId();
    if (id === null || this.deleting()) {
      return;
    }
    this.deleting.set(true);
    this.writeError.set(null);
    this.api
      .deletePolicyHolder(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deleting.set(false);
          this.confirmingDelete.set(false);
          // The holder is gone from every read, so staying put would show a 404 page the user
          // just caused; the list is where a deleted row is expected to be missing from.
          void this.router.navigate(['/policyholders']);
        },
        error: (failure: ApiError) => {
          this.deleting.set(false);
          this.writeError.set(failure);
        },
      });
  }

  protected restore(): void {
    const id = this.holderId();
    if (id === null || this.restoring()) {
      return;
    }
    this.restoring.set(true);
    this.writeError.set(null);
    this.api
      .restorePolicyHolder(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.restoring.set(false);
          this.holderResource()?.reload();
          this.policiesResource()?.reload();
        },
        error: (failure: ApiError) => {
          this.restoring.set(false);
          this.writeError.set(failure);
        },
      });
  }

  protected onStatusChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.state.setStatus(value === '' ? undefined : value);
    this.policiesResource()?.reload();
  }

  protected goToPage(page: number): void {
    this.state.goToPage(page);
    this.policiesResource()?.reload();
  }

  protected setPageSize(size: number): void {
    this.state.setPageSize(size);
    this.policiesResource()?.reload();
  }

  /** "No policies" and "no such holder" are different answers, and the header already says which. */
  protected emptyPoliciesMessage(): string {
    const filter = this.state.status();
    const scope = filter === undefined ? 'any status' : `status ${filter}`;
    if (this.holder() === null) {
      return 'There is no policyholder on this page, so there is nothing to list policies for.';
    }
    return `This policyholder owns no policies matching ${scope}.`;
  }

  /**
   * The API sends a bare number with no currency code, so it is rendered as a grouped figure rather
   * than guessed at with a symbol the data does not support.
   */
  protected premium(amount: number): string {
    return amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  /**
   * The rowversion is a base64 concurrency token the API compares on write. It is shown truncated
   * because it is long and meaningless to read, and whole because an operator comparing two screens
   * needs the full value.
   */
  protected shortToken(rowVersion: string | null): string {
    if (rowVersion === null || rowVersion.length === 0) {
      return 'none';
    }
    return rowVersion.length <= 10 ? rowVersion : `${rowVersion.slice(0, 10)}…`;
  }

  private resetForms(): void {
    this.firstName.set('');
    this.lastName.set('');
    this.email.set('');
    this.saveError.set(null);
    this.saveNote.set('');
    this.writeError.set(null);
    this.confirmingDelete.set(false);
  }
}

/** The id comes off the URL as a string; anything the API could not have issued becomes null. */
function toHolderId(raw: string): number | null {
  const parsed = Number(raw);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
}
