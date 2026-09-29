import { DecimalPipe } from '@angular/common';
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
import { RouterLink } from '@angular/router';

import { AuthService } from '../../core/auth.service';
import { Claim, ClaimStatus } from '../../core/models';
import { ApiError } from '../../core/problem-details';
import { PolicyManagerApi } from '../../core/policy-manager-api.service';
import { Resource } from '../../core/resource';
import { Badge } from '../../shared/badge';
import { ErrorBanner } from '../../shared/error-banner';

/**
 * The only statuses a claim can move to.
 *
 * `Pending` is deliberately absent. `ClaimStatusTransitions` permits transitions out of `Pending`
 * only, and `Approved` and `Denied` are terminal, so offering `Pending` as a target would be offering
 * the one transition the API refuses — a control that can only ever answer 422. The rule is stated in
 * the form rather than encoded as a missing option, so the reason a decided claim cannot be re-opened
 * is visible instead of merely felt.
 */
const DECISION_STATUSES: readonly ClaimStatus[] = ['Approved', 'Denied'];

/**
 * One claim: its facts, the one decision that can be taken on it, and the delete/restore pair.
 *
 * The two lifecycle states are the whole design of this page. Before adjudication a claim is
 * provisional: it can be deleted, and deleting it releases the amount it was reserving on its
 * policy. After adjudication it is final — the API permits no transition out of `Approved` or
 * `Denied`, and `DELETE` on one answers 409. So the delete control is offered for a pending claim
 * only, and the adjudication form disables itself with a stated reason once the decision is on the
 * row, rather than letting a second `PATCH` be sent and refused.
 *
 * Restore is deliberately reachable when the claim itself failed to load. A soft delete is filtered
 * out of reads by a global query filter, so the load 404s for exactly the rows restore exists to
 * recover — a Restore hidden behind a successful GET would be the one control that can never appear
 * for the rows it exists for. The 404 and a mistyped id are the same answer, so the page states both
 * and offers the recovery rather than guessing which one it was.
 *
 * The `rowVersion` is displayed but never parsed. It is an opaque base64 concurrency token; the only
 * correct thing to do with it is echo it back on the next write, and showing it is what lets an
 * operator correlate this row with a server log.
 */
@Component({
  selector: 'app-claim-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, FormsModule, RouterLink, Badge, ErrorBanner],
  template: `
    <p class="crumbs"><a routerLink="/claims">Claims</a></p>

    @if (claim(); as current) {
      <h1 class="lead">{{ current.claimNumber }}</h1>
    } @else if (notFound()) {
      <h1>Claim {{ id() }}</h1>
    } @else {
      <h1>Claim</h1>
    }

    @if (claimLoading() && !claim()) {
      <p class="muted">Loading…</p>
    }

    @if (claimError() && !notFound()) {
      <app-error-banner [error]="claimError()" />
    }

    @if (notFound()) {
      <div class="card missing">
        <p class="headline">There is no claim with that identifier.</p>
        <p class="muted">
          The API answered 404 for <code>{{ id() }}</code
          >. Claim identifiers are assigned by the server, so one from another environment will not
          resolve here — or the claim is a soft deleted one, which reads are filtered away from.
          Deleting a claim is reversible, and Restore brings it back exactly as it was.
        </p>
        @if (canRestore()) {
          <button type="button" class="primary" (click)="restore()" [disabled]="restoring()">
            {{ restoring() ? 'Restoring…' : 'Restore this claim' }}
          </button>
          <p class="hint">
            Restoring re-reserves the claim's amount on its policy, which may by now sit inside a
            coverage limit that other claims have since consumed. The claim returns regardless.
          </p>
        } @else {
          <p class="hint">Restoring needs an Admin or Adjuster token; this one is not eligible.</p>
        }
        <app-error-banner [error]="writeError()" />
        <p><a routerLink="/claims">Back to the claim list</a></p>
      </div>
    } @else if (claim(); as current) {
      <div class="card">
        <dl class="facts">
          <div>
            <dt>Claim number</dt>
            <dd class="strong">{{ current.claimNumber }}</dd>
          </div>
          <div>
            <dt>Status</dt>
            <dd>
              <app-badge [value]="current.status" />
              @if (adjudicated()) {
                <span class="muted">final</span>
              }
            </dd>
          </div>
          <div>
            <dt>Amount</dt>
            <dd class="numeric">{{ current.amount | number: '1.2-2' }}</dd>
          </div>
          <div>
            <dt>Policy</dt>
            <dd>
              <a [routerLink]="['/policies', current.policyId]">#{{ current.policyId }}</a>
            </dd>
          </div>
          <div>
            <dt>Filed</dt>
            <dd>
              {{ asDateTime(current.filedAt) }}
              @if (current.updatedBy) {
                <span class="muted">by {{ current.updatedBy }}</span>
              }
            </dd>
          </div>
          @if (current.decisionDate) {
            <div>
              <dt>Decided</dt>
              <dd>
                {{ asDateTime(current.decisionDate) }}
                @if (current.decidedBy) {
                  <span class="muted">by {{ current.decidedBy }}</span>
                }
              </dd>
            </div>
          }
          <div>
            <dt>Last updated</dt>
            <dd>
              @if (current.updatedAt) {
                {{ asDateTime(current.updatedAt) }}
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

        <p class="description">{{ current.description }}</p>
      </div>

      @if (current.adjusterNotes) {
        <div class="card">
          <h2>Adjuster notes</h2>
          <p class="description">{{ current.adjusterNotes }}</p>
        </div>
      }

      @if (canAdjudicate()) {
        <form class="card" (ngSubmit)="adjudicate()">
          <h2>Adjudicate</h2>
          <p class="muted note">
            This is a one-way door. The API permits a transition out of Pending only, so an approved
            or denied claim can never be re-opened — and it can never be deleted afterwards either.
            Choose with that in mind.
          </p>

          @if (adjudicateError(); as failure) {
            <app-error-banner [error]="failure" />
          }
          @if (conflicted()) {
            <p class="conflict" role="alert">
              Someone else changed this claim after you loaded it, so your decision was not applied.
              Reload the page to see the current state before deciding again.
            </p>
          }

          <div class="fields">
            <div class="field">
              <label for="decision-status">Decision</label>
              <select id="decision-status" name="status" [(ngModel)]="statusDraft" required>
                @for (option of decisionStatuses; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>
              @if (fieldError('Status'); as message) {
                <p class="field-error" role="alert">{{ message }}</p>
              }
            </div>

            <div class="field">
              <label for="decision-by">Decided by</label>
              <input
                id="decision-by"
                name="decidedBy"
                type="text"
                maxlength="100"
                [(ngModel)]="decidedByDraft"
                placeholder="Adjuster identifier"
              />
              <p class="hint">Optional. The API also records the decision timestamp itself.</p>
              @if (fieldError('DecidedBy'); as message) {
                <p class="field-error" role="alert">{{ message }}</p>
              }
            </div>

            <div class="field wide">
              <label for="decision-notes">Notes</label>
              <textarea
                id="decision-notes"
                name="notes"
                rows="3"
                maxlength="1000"
                [(ngModel)]="notesDraft"
                placeholder="Why the claim was decided this way"
              ></textarea>
              <p class="hint">Optional, up to 1000 characters.</p>
              @if (fieldError('Notes'); as message) {
                <p class="field-error" role="alert">{{ message }}</p>
              }
            </div>
          </div>

          <button type="submit" class="primary" [disabled]="adjudicating()">
            {{ adjudicating() ? 'Recording…' : 'Record decision' }}
          </button>
        </form>
      } @else if (canWrite()) {
        <div class="card finalised">
          <h2>Adjudication</h2>
          <p class="muted note" role="status">
            This claim was
            {{ current.status === 'Approved' ? 'approved' : 'denied' }}
            {{ current.decisionDate ? 'on ' + asDateTime(current.decisionDate) : '' }} and that
            decision is final. The API permits no transition out of {{ current.status }}, so there
            is nothing left to record — and the claim cannot be deleted either.
          </p>
        </div>
      }

      @if (canDelete()) {
        <div class="card danger-zone">
          <h2>Delete this claim</h2>
          <p class="muted note">
            A soft delete: the claim disappears from every read and the
            {{ current.amount | number: '1.2-2' }} it was reserving on policy #{{
              current.policyId
            }}
            is released back into that policy's remaining cover. The row survives, and
            <strong>Restore</strong> on this page brings it back as it was. Only a claim still
            awaiting adjudication can be deleted — an approved or denied one is refused.
          </p>

          @if (writeError(); as failure) {
            <app-error-banner [error]="failure" />
          }

          @if (confirmingDelete()) {
            <div class="confirm" role="alert">
              <p>
                Delete claim {{ current.claimNumber }}? It can be restored from this page
                afterwards.
              </p>
              <button type="button" class="danger" (click)="remove()" [disabled]="deleting()">
                {{ deleting() ? 'Deleting…' : 'Yes, delete it' }}
              </button>
              <button type="button" (click)="confirmingDelete.set(false)">Keep it</button>
            </div>
          } @else {
            <button type="button" class="danger" (click)="confirmingDelete.set(true)">
              Delete this claim…
            </button>
          }
        </div>
      } @else if (canWrite()) {
        <div class="card finalised">
          <h2>Delete</h2>
          <p class="muted note">
            Not available. This claim is {{ current.status.toLowerCase() }}, and adjudication is
            final — the API answers 409 to a delete of a decided claim, so the control is withheld
            rather than offered as a way to find that out.
          </p>
        </div>
      }
    }
  `,
  styles: `
    .crumbs {
      margin: 0 0 0.25rem;
      font-size: 0.875rem;
    }
    .lead {
      margin-bottom: 1rem;
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
    .description {
      margin: 1rem 0 0;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
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
    .hint {
      margin: 0;
      font-size: 0.8125rem;
      color: #71717a;
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
    .danger-zone {
      border-color: #fecdca;
    }
    .finalised {
      border-color: #e4e4e7;
      background: #fafafa;
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
  `,
})
export class ClaimDetail {
  private readonly api = inject(PolicyManagerApi);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Bound from the `:id` route segment by `withComponentInputBinding()`, so it arrives as a string
   * even when the route is navigated to with a number. Converting here rather than in the template
   * keeps the API service taking the number it declares.
   */
  readonly id = input.required<string>();

  protected readonly decisionStatuses = DECISION_STATUSES;

  private readonly claimId = signal<number | null>(null);
  private readonly claimResource = signal<Resource<Claim> | null>(null);

  protected readonly claim = computed(() => this.claimResource()?.data() ?? null);
  protected readonly claimError = computed(() => this.claimResource()?.error() ?? null);
  protected readonly claimLoading = computed(() => this.claimResource()?.loading() ?? false);

  /**
   * A 404 on the claim is not a generic failure: a global read filter hides soft-deleted rows, so
   * this is what a deleted claim looks like from here — the same answer as a mistyped id, which is
   * why the page offers the restore that distinguishes them.
   */
  protected readonly notFound = computed(() => this.claimError()?.status === 404);

  protected readonly statusDraft = signal<ClaimStatus>('Approved');
  protected readonly notesDraft = signal('');
  protected readonly decidedByDraft = signal('');
  protected readonly adjudicating = signal(false);
  protected readonly adjudicateError = signal<ApiError | null>(null);

  protected readonly writeError = signal<ApiError | null>(null);
  protected readonly confirmingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly restoring = signal(false);

  constructor() {
    // Built here rather than as a field initialiser: `Resource` fetches immediately on construction,
    // and a required input has no value yet while the constructor runs. An effect first executes
    // during change detection, by which point the router has bound `id`.
    //
    // Navigating between two claims reuses this component and only rebinds `id`, so a fresh resource
    // replaces the old one. The old one stays subscribed until the component is destroyed, which
    // costs one in-flight request at most and cannot repaint anything: nothing reads a resource that
    // is no longer the one held by the signal.
    effect(() => {
      const parsed = toClaimId(this.id());
      untracked(() => {
        this.claimId.set(parsed);
        this.claimResource.set(null);
        this.resetForms();
        if (parsed === null) {
          return;
        }
        // A route that is not a positive integer never names a claim, so nothing is asked of the API
        // for it at all — the template explains the identifier rather than showing a 400 for it.
        this.claimResource.set(new Resource(() => this.api.getClaim(parsed), this.destroyRef));
      });
    });

    // Prefills the adjudication form from whatever loaded, so a re-decided claim shows the values
    // the server holds rather than the form's own defaults.
    effect(() => {
      const current = this.claim();
      if (current === null) {
        return;
      }
      untracked(() => {
        this.statusDraft.set(current.status === 'Denied' ? 'Denied' : 'Approved');
        this.notesDraft.set(current.adjusterNotes ?? '');
        this.decidedByDraft.set(current.decidedBy ?? '');
        this.adjudicateError.set(null);
      });
    });
  }

  protected canWrite(): boolean {
    return this.auth.hasRole('Admin', 'Adjuster');
  }

  /** Anything that is not still `Pending` has been decided, and a decision is final. */
  protected adjudicated(): boolean {
    const current = this.claim();
    return current !== null && current.status !== 'Pending';
  }

  /** The form only exists while there is a decision left to take. */
  protected canAdjudicate(): boolean {
    return this.canWrite() && this.claim() !== null && !this.adjudicated();
  }

  /**
   * Only a claim awaiting adjudication can be deleted. An approved or denied one answers 409, so the
   * control is withheld instead of being offered as a way to discover that.
   */
  protected canDelete(): boolean {
    return this.canWrite() && this.claim() !== null && !this.adjudicated();
  }

  /**
   * Offered whenever the claim could not be read, which is the only state Restore can act on — a
   * live claim is not deleted, and a restore of one would be refused. The API's restore is
   * idempotent, so pressing this on a claim that was never deleted is a no-op rather than an error.
   */
  protected canRestore(): boolean {
    return this.canWrite() && this.claimId() !== null && this.claim() === null;
  }

  /** A stale token is a 409 and means the row moved underneath this page, which is worth saying. */
  protected conflicted(): boolean {
    return this.adjudicateError()?.status === 409;
  }

  /** Field-level messages from a 400, keyed the way the API names the DTO properties. */
  protected fieldError(field: string): string | null {
    const messages = this.adjudicateError()?.errors[field];
    return messages && messages.length > 0 ? messages.join(' ') : null;
  }

  protected adjudicate(): void {
    const current = this.claim();
    if (current === null || this.adjudicating() || this.adjudicated()) {
      return;
    }
    const status = this.statusDraft();
    if (!DECISION_STATUSES.includes(status)) {
      return;
    }

    this.adjudicating.set(true);
    this.adjudicateError.set(null);

    const notes = this.notesDraft().trim();
    const decidedBy = this.decidedByDraft().trim();

    this.api
      .updateClaimStatus(current.id, {
        status,
        // Omitted rather than sent as an empty string: the API takes both as optional, and a blank
        // notes field is an absent note, not a note that happens to be empty.
        ...(notes === '' ? {} : { notes }),
        ...(decidedBy === '' ? {} : { decidedBy }),
        // The token makes the write conditional on nothing having changed since this row was read.
        // A claim with no token predates concurrency, so the write goes through unconditionally
        // rather than being blocked on a token the server never issued.
        ...(current.rowVersion === null ? {} : { rowVersion: current.rowVersion }),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.adjudicating.set(false);
          // Reloaded rather than patched locally: the PATCH answers 200 with no body, and the
          // refreshed row brings the decision date, the decided-by and the new rowVersion with it.
          this.claimResource()?.reload();
        },
        error: (failure: ApiError) => {
          this.adjudicating.set(false);
          this.adjudicateError.set(failure);
        },
      });
  }

  protected remove(): void {
    const current = this.claim();
    if (current === null || this.deleting()) {
      return;
    }
    this.deleting.set(true);
    this.writeError.set(null);

    this.api
      .deleteClaim(current.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deleting.set(false);
          this.confirmingDelete.set(false);
          // The claim is hidden from every read now, so the row this page is showing is no longer a
          // thing that exists. Reloading turns the same view into the 404 state, which is where
          // Restore is offered — staying on a rendered claim that the API will not return would be a
          // lie the next reload would expose anyway.
          this.claimResource()?.reload();
        },
        error: (failure: ApiError) => {
          this.deleting.set(false);
          this.writeError.set(failure);
        },
      });
  }

  protected restore(): void {
    const id = this.claimId();
    if (id === null || this.restoring()) {
      return;
    }
    this.restoring.set(true);
    this.writeError.set(null);

    this.api
      .restoreClaim(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.restoring.set(false);
          this.claimResource()?.reload();
        },
        error: (failure: ApiError) => {
          this.restoring.set(false);
          this.writeError.set(failure);
        },
      });
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

  private resetForms(): void {
    this.statusDraft.set('Approved');
    this.notesDraft.set('');
    this.decidedByDraft.set('');
    this.adjudicateError.set(null);
    this.writeError.set(null);
    this.confirmingDelete.set(false);
    this.deleting.set(false);
    this.restoring.set(false);
  }
}

/** The route parameter as a claim identifier, or null when it is not one. */
function toClaimId(raw: string): number | null {
  const parsed = Number(raw);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
}
