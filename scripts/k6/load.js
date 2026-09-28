// k6 load test: read-heavy mixed workload.
//
//   k6 run -e BASE_URL=http://localhost:8080 \
//          -e API_TOKEN="$(node ../mint-token.ts)" load.js
//
// Default: ramp to 50 VUs over 2m, hold 5m, ramp down. ~90% paged list reads
// (policyholders/policies/claims), ~10% writes (holder create only, so the
// run needs no fixture data and never trips coverage or business rules).
// Tune without editing the file:
//
//   k6 run -e BASE_URL=... -e VUS_MAX=20 -e VUS_WRITE_MAX=2 -e HOLD=2m load.js
//
// READ THE RATE-LIMIT THRESHOLD BELOW BEFORE TRUSTING THE ERROR RATE.
import http from 'k6/http';
import { check } from 'k6/check';
import { sleep } from 'k6';
import { BASE_URL, headersFor } from './common.js';

const VUS_MAX = Number(__ENV.VUS_MAX || 50);
const VUS_WRITE_MAX = Number(__ENV.VUS_WRITE_MAX || 5);
const HOLD = __ENV.HOLD || '5m';

export const options = {
  scenarios: {
    reads: {
      executor: 'ramping-vus',
      exec: 'reads',
      startVUs: 0,
      stages: [
        { duration: '2m', target: VUS_MAX },
        { duration: HOLD, target: VUS_MAX },
        { duration: '1m', target: 0 },
      ],
    },
    writes: {
      executor: 'ramping-vus',
      exec: 'writes',
      startVUs: 0,
      stages: [
        { duration: '2m', target: VUS_WRITE_MAX },
        { duration: HOLD, target: VUS_WRITE_MAX },
        { duration: '1m', target: 0 },
      ],
    },
  },
  thresholds: {
    // The API rejects a caller that exceeds RateLimiting__PermitLimit (100/min by default) with a
    // 429, and a 429 is a failed request as far as these metrics are concerned. So a run against
    // the default quota is measuring the limiter, not the API, and will report a ~99% error rate
    // while the service is perfectly healthy. Raise the quota for the run — the API reads it from
    // configuration, so `RateLimiting__PermitLimit=100000` on the server is enough — or lower
    // VUS_MAX until the run fits inside it. This is a property of load-testing a rate-limited API,
    // not a defect to tune away.
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<800'],
  },
};

const LISTS = [
  '/api/policyholders?page=1&pageSize=25',
  '/api/policyholders?page=3&pageSize=25&sortBy=lastName',
  '/api/policies?page=1&pageSize=25',
  '/api/policies?status=Active&page=1&pageSize=25',
  '/api/claims?page=1&pageSize=25',
];

export function reads() {
  const path = LISTS[Math.floor(Math.random() * LISTS.length)];
  const res = http.get(`${BASE_URL}${path}`, { headers: headersFor(`read-${__VU}`) });
  check(res, { [`GET ${path} -> 200`]: (r) => r.status === 200 });
  sleep(0.2);
}

export function writes() {
  const tag = `load-${__VU}-${__ITER}-${Date.now()}`;
  const res = http.post(
    `${BASE_URL}/api/policyholders`,
    JSON.stringify({ firstName: 'Load', lastName: 'Test', email: `${tag}@example.com` }),
    { headers: headersFor(`write-${__VU}`) },
  );
  check(res, { 'POST holder -> 201': (r) => r.status === 201 });
  sleep(1);
}
