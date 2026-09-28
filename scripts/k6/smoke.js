// k6 smoke test: end-to-end lifecycle against a live API.
//
//   k6 run -e BASE_URL=http://localhost:8080 \
//          -e API_TOKEN="$(node ../mint-token.ts)" smoke.js
//
// 1 VU, 1 iteration: create policyholder -> issue policy -> file claim ->
// adjudicate -> verify reads. Fails fast on any unexpected status so it can
// gate a deployment or a local bring-up. Expects a MUTABLE database: it
// writes rows. Point it at a scratch database, not production.
//
// The token needs a role that may create policyholders and adjudicate claims,
// because the whole lifecycle is in one script: Admin or Adjuster. An Agent
// token is refused on the first and last write.
import http from 'k6/http';
import { check, fail } from 'k6/check';
import { BASE_URL, headersFor } from './common.js';

export const options = {
  vus: 1,
  iterations: 1,
  thresholds: {
    http_req_failed: ['rate==0'],
  },
};

function must(name, res, want) {
  const ok = check(res, { [`${name} -> ${want}`]: (r) => r.status === want });
  if (!ok) fail(`${name}: got ${res.status} ${res.body && res.body.slice(0, 300)}`);
  return res.body ? JSON.parse(res.body) : null;
}

export default function () {
  const tag = `smoke-${__VU}-${Date.now()}`;
  const H = headersFor(tag);

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
        // Large enough that the claim below cannot exhaust it: a 422 here would be the coverage
        // rule working, not the API being broken.
        coverageLimit: 500000,
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

  // Pending -> Approved is the only legal transition out of a new claim, and the endpoint answers
  // 200 with no body, which is why this step does not parse one.
  const adjudication = http.patch(
    `${BASE_URL}/api/claims/${claim.id}/status`,
    JSON.stringify({ status: 'Approved', decidedBy: 'k6-smoke', notes: 'smoke test' }),
    { headers: H },
  );
  if (!check(adjudication, { 'adjudicate claim -> 200': (r) => r.status === 200 })) {
    fail(`adjudicate claim: got ${adjudication.status} ${adjudication.body && adjudication.body.slice(0, 300)}`);
  }

  must('read claim', http.get(`${BASE_URL}/api/claims/${claim.id}`, { headers: H }), 200);
  must('list policies', http.get(`${BASE_URL}/api/policies?page=1&pageSize=5`, { headers: H }), 200);
  must('list claims', http.get(`${BASE_URL}/api/claims?page=1&pageSize=5`, { headers: H }), 200);
}
