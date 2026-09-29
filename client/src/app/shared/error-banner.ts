import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { ApiError } from '../core/problem-details';

/**
 * Renders a failure the way an operator needs it: what went wrong, and the correlation ID to quote
 * in a log search.
 *
 * The correlation ID is the reason this is a component rather than an `alert()` — it is the one
 * piece of a problem details body that makes the failure findable, and hiding it behind a console
 * log is what makes a report untriageable.
 */
@Component({
  selector: 'app-error-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error(); as failure) {
      <div class="banner" role="alert">
        <div class="body">
          <p class="message">{{ failure.displayMessage }}</p>
          @if (failure.status > 0) {
            <p class="status">HTTP {{ failure.status }}</p>
          }
        </div>
        @if (failure.correlationId) {
          <p class="correlation">
            <span class="label">Correlation ID</span>
            <code>{{ failure.correlationId }}</code>
          </p>
        }
      </div>
    }
  `,
  styles: `
    .banner {
      border: 1px solid #b42318;
      border-left-width: 4px;
      border-radius: 4px;
      background: #fef3f2;
      padding: 0.75rem 1rem;
      margin: 0 0 1rem;
    }
    .message {
      margin: 0;
      color: #7a271a;
    }
    .status {
      margin: 0.25rem 0 0;
      font-size: 0.8125rem;
      color: #7a271a;
    }
    .correlation {
      margin: 0.5rem 0 0;
      font-size: 0.8125rem;
      color: #7a271a;
      display: flex;
      gap: 0.5rem;
      align-items: baseline;
      flex-wrap: wrap;
    }
    .label {
      text-transform: uppercase;
      letter-spacing: 0.04em;
      font-size: 0.6875rem;
    }
  `,
})
export class ErrorBanner {
  readonly error = input.required<ApiError | null>();
}
