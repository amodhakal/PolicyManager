import { Injectable, computed, inject, signal } from '@angular/core';

const TOKEN_KEY = 'policymanager.token';

/**
 * The roles the API's authorization policies recognise (`PolicyRoles` in
 * `PolicyManager/Services/JwtAuthentication.cs`).
 */
export type Role = 'Admin' | 'Adjuster' | 'Agent';

const KNOWN_ROLES: readonly Role[] = ['Admin', 'Adjuster', 'Agent'];

/**
 * The wire form of `ClaimTypes.Role`, which is the long URI rather than the word "role" — .NET's
 * default claim mapping does not shorten it, so a token minted by the standard tooling carries this
 * key and nothing named `role`.
 */
const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

/** The claims read out of the bearer token, used only to render the UI the token can reach. */
export interface TokenClaims {
  roles: Role[];
  name: string | null;
  expiresAt: Date | null;
}

function decodeBase64Url(segment: string): string {
  const padded = segment.replace(/-/g, '+').replace(/_/g, '/');
  const withPadding = padded.padEnd(padded.length + ((4 - (padded.length % 4)) % 4), '=');
  return atob(withPadding);
}

/**
 * Holds the bearer token the API requires on every endpoint.
 *
 * The API has no login endpoint — it validates externally minted JWTs and offers nothing to issue
 * them — so the token is pasted in rather than obtained. It is kept in `localStorage` so a reload
 * does not ask for it again, which is a deliberate trade: convenient for a developer tool pointed at
 * a local API, and wrong for anything handling real data, which is why this is a client for a dev
 * environment rather than a product.
 *
 * The payload is decoded but never trusted. Nothing here grants or refuses an action; it only decides
 * which controls to show. The API is the only thing that enforces a permission, and a user who
 * edits `localStorage` sees buttons that then answer 403.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _token = signal<string | null>(readStoredToken());
  private readonly _claims = signal<TokenClaims | null>(null);

  readonly token = this._token.asReadonly();
  readonly isAuthenticated = computed(() => this._token() !== null);
  readonly claims = computed(() => {
    // Recomputed whenever the token changes; the decoded payload is derived, never stored twice.
    this._token();
    return this._claims();
  });
  readonly roles = computed(() => this._claims()?.roles ?? []);

  constructor() {
    this._claims.set(decodeClaims(this._token()));
  }

  /**
   * Accepts a pasted token. Returns the reason it was rejected, or null when it parsed.
   *
   * A token whose payload cannot be read is refused rather than stored: sending garbage to the API
   * produces a 401 that looks like a permissions problem, and the operator needs to be told which of
   * the two it is.
   */
  signIn(token: string): string | null {
    const trimmed = token.trim();
    if (trimmed.length === 0) {
      return 'Paste a bearer token.';
    }
    const claims = decodeClaims(trimmed);
    if (claims === null) {
      return 'That is not a readable JWT. Copy the whole token, not a fragment of it.';
    }
    if (claims.expiresAt !== null && claims.expiresAt.getTime() <= Date.now()) {
      return 'That token has already expired.';
    }
    if (claims.roles.length === 0) {
      return 'That token carries none of the roles this API recognises (Admin, Adjuster, Agent).';
    }

    localStorage.setItem(TOKEN_KEY, trimmed);
    this._token.set(trimmed);
    this._claims.set(claims);
    return null;
  }

  signOut(): void {
    localStorage.removeItem(TOKEN_KEY);
    this._token.set(null);
    this._claims.set(null);
  }

  /** Whether the token carries a role the API would let past the given policy. */
  hasRole(...allowed: readonly Role[]): boolean {
    const held = this.roles();
    return allowed.some((role) => held.includes(role));
  }
}

function readStoredToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY);
  } catch {
    // A browser with storage disabled (private mode, a locked-down profile) still gets a session —
    // it just does not survive a reload.
    return null;
  }
}

function decodeClaims(token: string | null): TokenClaims | null {
  if (token === null) {
    return null;
  }
  const segments = token.split('.');
  if (segments.length !== 3) {
    return null;
  }
  try {
    const payload = JSON.parse(decodeBase64Url(segments[1])) as Record<string, unknown>;

    // The API reads roles through ClaimTypes.Role, which is the long URI form on the wire, and a
    // token minted by another library may use the short "role" form. Both spellings are accepted;
    // an unknown one is dropped rather than guessed at.
    // Bracketed throughout because tsconfig sets noPropertyAccessFromIndexSignature: a JWT payload
    // is attacker-influenced data whose shape is not known at compile time, and dot access on an
    // index signature is exactly the access this flag exists to prevent.
    const rawRoles = [payload['role'], payload['roles'], payload[ROLE_CLAIM]]
      .flatMap((value) => (Array.isArray(value) ? value : value === undefined ? [] : [value]))
      .map((value) => String(value))
      .filter((value): value is Role => KNOWN_ROLES.includes(value as Role));

    const exp = typeof payload['exp'] === 'number' ? new Date(payload['exp'] * 1000) : null;
    const name =
      typeof payload['name'] === 'string'
        ? payload['name']
        : typeof payload['unique_name'] === 'string'
          ? payload['unique_name']
          : typeof payload['sub'] === 'string'
            ? payload['sub']
            : null;

    return { roles: [...new Set(rawRoles)], name, expiresAt: exp };
  } catch {
    return null;
  }
}
