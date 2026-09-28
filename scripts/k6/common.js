// Shared setup for the k6 suites: where the API is, and the bearer token every request carries.
//
// Every /api endpoint requires a token, so a suite that sends none measures 401s rather than the
// API. Two ways to supply one:
//
//   k6 run -e API_TOKEN="$(node scripts/mint-token.ts)" -e BASE_URL=... scripts/k6/smoke.js
//
// or let the suite mint one itself from the API's signing key, which is what happens when
// API_TOKEN is absent and JWT_SIGNING_KEY is set:
//
//   k6 run -e JWT_SIGNING_KEY=... -e JWT_ROLE=Admin -e BASE_URL=... scripts/k6/smoke.js
//
// k6 runs plain JavaScript and has no Web Crypto, so the HS256 signature is computed with the
// k6 crypto module and base64url encoding by hand. The claim names are the short ones, because the
// API's inbound claim mapping rewrites "role" and "unique_name" to the long ClaimTypes URIs its
// authorization policies and audit columns read.
import encoding from 'k6/encoding';
import crypto from 'k6/crypto';

const DEFAULT_BASE_URL = 'http://localhost:8080';

function envNumber(name, fallback) {
  const raw = __ENV[name];
  if (raw === undefined || raw === '') return fallback;
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function mintToken() {
  const key = __ENV.JWT_SIGNING_KEY;
  if (!key) {
    throw new Error(
      'No bearer token: set API_TOKEN to a token you have, or JWT_SIGNING_KEY to the API\'s ' +
        'Jwt:SigningKey (at least 32 bytes) so this suite can mint one. See scripts/README.md.',
    );
  }

  const now = Math.floor(Date.now() / 1000);
  const header = encoding.b64encode(JSON.stringify({ alg: 'HS256', typ: 'JWT' }), 'rawurl');
  const payload = encoding.b64encode(
    JSON.stringify({
      iss: __ENV.JWT_ISSUER || 'policy-manager',
      aud: __ENV.JWT_AUDIENCE || 'policy-manager-api',
      sub: __ENV.JWT_SUBJECT || 'k6',
      unique_name: __ENV.JWT_SUBJECT || 'k6',
      role: (__ENV.JWT_ROLE || 'Admin').split(',').map((r) => r.trim()).filter((r) => r.length > 0),
      iat: now,
      nbf: now,
      exp: now + envNumber('JWT_TTL_SECONDS', 3600),
    }),
    'rawurl',
  );

  const signingInput = `${header}.${payload}`;
  const signature = encoding.b64encode(
    crypto.hmac('sha256', key, signingInput, 'raw'),
    'rawurl',
  );

  return `${signingInput}.${signature}`;
}

export const BASE_URL = (__ENV.BASE_URL || DEFAULT_BASE_URL).replace(/\/+$/, '');
export const TOKEN = __ENV.API_TOKEN && __ENV.API_TOKEN.length > 0 ? __ENV.API_TOKEN : mintToken();

export const JSON_HEADERS = { 'Content-Type': 'application/json' };
export const AUTH_HEADERS = Object.assign({ Authorization: `Bearer ${TOKEN}` }, JSON_HEADERS);

// A distinct correlation ID per VU. The API counts its rate-limit quota against the correlation ID
// when one is supplied, and against the connection's address otherwise, so a suite that omits the
// header shares one bucket across the whole run and starts answering 429. Point the quota at
// something that can carry the run (RateLimiting__PermitLimit) rather than at the default 100.
export function headersFor(tag) {
  return Object.assign({ 'X-Correlation-ID': `k6-${tag}` }, AUTH_HEADERS);
}
