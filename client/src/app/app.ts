import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { AuthService } from './core/auth.service';

/**
 * The application shell: navigation, the signed-in identity, and the routed view.
 *
 * The nav is filtered by role rather than hiding nothing, because a control the token cannot use is
 * a control that answers 403. The API is still the only thing that enforces it — this is about not
 * offering what cannot work.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <a class="brand" routerLink="/">Policy Manager</a>

      @if (auth.isAuthenticated()) {
        <nav aria-label="Main">
          @for (item of visibleLinks(); track item.path) {
            <a [routerLink]="item.path" routerLinkActive="active">{{ item.label }}</a>
          }
        </nav>

        <div class="identity">
          <span class="who">
            {{ auth.claims()?.name ?? 'Signed in' }}
            @if (roles().length > 0) {
              <span class="roles">({{ roles().join(', ') }})</span>
            }
          </span>
          <button type="button" (click)="signOut()">Sign out</button>
        </div>
      }
    </header>

    <main>
      <router-outlet />
    </main>
  `,
  styles: `
    .topbar {
      display: flex;
      align-items: center;
      gap: 1.5rem;
      padding: 0.625rem 1.5rem;
      background: var(--surface);
      border-bottom: 1px solid var(--border);
      flex-wrap: wrap;
    }
    .brand {
      font-weight: 600;
      text-decoration: none;
      color: var(--text);
    }
    nav {
      display: flex;
      gap: 0.25rem;
      flex: 1;
      flex-wrap: wrap;
    }
    nav a {
      padding: 0.25rem 0.625rem;
      border-radius: 4px;
      text-decoration: none;
      color: #3f3f46;
      font-size: 0.9375rem;
    }
    nav a:hover {
      background: #f4f4f5;
    }
    nav a.active {
      background: #eff8ff;
      color: var(--accent);
      font-weight: 500;
    }
    .identity {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      font-size: 0.875rem;
    }
    .who {
      color: #3f3f46;
    }
    .roles {
      color: var(--text-muted);
    }
    main {
      max-width: 72rem;
      margin: 0 auto;
      padding: 1.5rem;
    }
  `,
})
export class App {
  protected readonly auth = inject(AuthService);

  protected readonly roles = computed(() => this.auth.roles());

  /** Every nav item is readable by any recognised role; writes are gated inside the pages. */
  protected readonly links = [
    { path: '/policyholders', label: 'Policyholders' },
    { path: '/policies', label: 'Policies' },
    { path: '/claims', label: 'Claims' },
    { path: '/reports', label: 'Reports' },
  ] as const;

  protected readonly visibleLinks = computed(() => (this.auth.isAuthenticated() ? this.links : []));

  protected signOut(): void {
    this.auth.signOut();
    // A full navigation rather than a router call, so the guard re-runs and no component keeps a
    // rendered page from the previous session on screen behind the sign-in page.
    globalThis.location.assign('/sign-in');
  }
}
