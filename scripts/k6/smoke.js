// k6 smoke test: end-to-end lifecycle against a live API.
//
//   k6 run -e BASE_URL=http://localhost:8080 scripts/k6/smoke.js
//
// 1 VU, 1 iteration: create policyholder -> issue policy -> file claim ->
// adjudicate -> verify reads. Fails fast on any unexpected status so it can
// gate a deployment or a local bring-up. Expects a MUTABLE database: it
// writes rows. Point it at a scratch database, not production.
import http from 'k6/http';
import { check, fail } from 'k6/check';

export const options = {
  vus: 1,
  iterations: 1,
  thresholds: {
    http_req_failed: ['rate==0'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const H = { 'Content-Type': 'application/json' };

function must(name, res, want) {
  const ok = check(res, { [`${name} -> ${want}`]: (r) => r.status === want });
  if (!ok) fail(`${name}: got ${res.status} ${res.body && res.body.slice(0, 300)}`);
  return JSON.parse(res.body);
}

export default function () {
  const tag = `smoke-${__VU}-${Date.now()}`;

  const holder = must(
    'create policyholder',
    http.post(
      `${BASE_URL}/api/policyholders`,
      JSON.stringify({ firstName: 'Smoke', lastName: 'Test', email: `${tag}@example.com` }),
      { headers: H },
    ),
    201,
  );

  const policy = must(
    'issue policy',
    http.post(
      `${BASE_URL}/api/policies`,
      JSON.stringify({
        premium: 1200.5,
        policyHolderId: holder.id,
        type: 'Auto',
        coverageLimit: 50000,
        startDate: '2026-01-01T00:00:00Z',
        endDate: '2027-01-01T00:00:00Z',
      }),
      { headers: H },
    ),
    201,
  );

  const claim = must(
    'file claim',
    http.post(
      `${BASE_URL}/api/claims`,
      JSON.stringify({ policyId: policy.id, amount: 500, description: `smoke ${tag}` }),
      { headers: H },
    ),
    201,
  );

  must(
    'adjudicate claim',
    http.patch(
      `${BASE_URL}/api/claims/${claim.id}/status`,
      JSON.stringify({ status: 'Approved', decidedBy: 'k6-smoke', notes: 'smoke test' }),
      { headers: H },
    ),
    200,
  );

  must('read claim', http.get(`${BASE_URL}/api/claims/${claim.id}`), 200);
  must('list policies', http.get(`${BASE_URL}/api/policies?page=1&pageSize=5`), 200);
}
