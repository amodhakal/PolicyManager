import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

import { AuthService } from '../../core/auth.service';

/**
 * Where a bearer token is pasted in, because the API has none to log in with.
 *
 * The API validates externally minted JWTs and exposes no endpoint that issues one, so this page
 * exists to accept a token from whatever already mints them. The rejection reasons come from
 * `AuthService.signIn`, which parses the payload before storing it — a token that cannot be read
 * would otherwise be sent to the API and come back as a 401 that looks like a permissions problem.
 */
@Component({
  selector: 'app-sign-in',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="page">
      <div class="card sign-in">
        <h1>Policy Manager</h1>
        <p class="muted intro">
          The API requires a bearer token on every endpoint and has no login of its own, so paste a
          token issued elsewhere. It is kept in this browser's local storage.
        </p>

        <form (ngSubmit)="submit()">
          <label for="token">Bearer token</label>
          <textarea
            id="token"
            name="token"
            rows="5"
            [ngModel]="token()"
            (ngModelChange)="token.set($event)"
            placeholder="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
            spellcheck="false"
            autocomplete="off"
          ></textarea>

          @if (rejection(); as reason) {
            <p class="rejection" role="alert">{{ reason }}</p>
          }

          <button type="submit" class="primary" [disabled]="token().trim().length === 0">
            Continue
          </button>
        </form>

        <h2>Getting a token</h2>
        <p class="muted">
          The signing key, issuer and audience come from the API's <code>Jwt</code> configuration
          section. A token is only accepted if it is signed with that key and carries one of the
          roles the API recognises: <code>Admin</code>, <code>Adjuster</code> or <code>Agent</code>.
        </p>
      </div>
    </div>
  `,
  styles: `
    .page {
      max-width: 40rem;
      margin: 3rem auto;
      padding: 0 1rem;
    }
    .sign-in {
      padding: 1.5rem 1.75rem;
    }
    .intro {
      margin-top: 0;
      margin-bottom: 1.5rem;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      align-items: flex-start;
    }
    textarea {
      width: 100%;
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
      font-size: 0.8125rem;
      resize: vertical;
    }
    .rejection {
      margin: 0;
      color: #b42318;
      font-size: 0.875rem;
    }
    button {
      margin-top: 0.25rem;
    }
    h2 {
      margin-top: 2rem;
      font-size: 1rem;
    }
  `,
})
export class SignIn {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly token = signal('');
  protected readonly rejection = signal<string | null>(null);

  protected submit(): void {
    const reason = this.auth.signIn(this.token());
    if (reason !== null) {
      this.rejection.set(reason);
      return;
    }
    this.rejection.set(null);
    void this.router.navigateByUrl('/');
  }
}
