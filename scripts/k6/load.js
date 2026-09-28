// k6 load test: read-heavy mixed workload.
//
//   k6 run -e BASE_URL=http://localhost:8080 scripts/k6/load.js
//
// Default: ramp to 50 VUs over 2m, hold 5m, ramp down. ~90% paged list reads
// (policyholders/policies/claims), ~10% writes (holder create only, so the
// run needs no fixture data and never trips coverage/business rules).
// Tune VUS_* env knobs without editing the file:
//
//   k6 run -e BASE_URL=http://localhost:8080 -e VUS_MAX=20 -e HOLD=2m scripts/k6/load.js
import http from 'k6/http';
import { check } from 'k6/check';
import { sleep } from 'k6';

export const options = {
  scenarios: {
    reads: {
      executor: 'ramping-vus',
      exec: 'reads',
      startVUs: 0,
      stages: [
        { duration: '2m', target: __ENV.VUS_MAX ? Number(__ENV.VUS_MAX) : 50 },
        { duration: __ENV.HOLD || '5m', target: __ENV.VUS_MAX ? Number(__ENV.VUS_MAX) : 50 },
        { duration: '1m', target: 0 },
      ],
    },
    writes: {
      executor: 'ramping-vus',
      exec: 'writes',
      startVUs: 0,
      stages: [
        { duration: '2m', target: __ENV.VUS_WRITE_MAX ? Number(__ENV.VUS_WRITE_MAX) : 5 },
        { duration: __ENV.HOLD || '5m', target: __ENV.VUS_WRITE_MAX ? Number(__ENV.VUS_WRITE_MAX) : 5 },
        { duration: '1m', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<800'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const H = { 'Content-Type': 'application/json' };

const LISTS = [
  '/api/policyholders?page=1&pageSize=25',
  '/api/policyholders?page=3&pageSize=25&sortBy=lastName',
  '/api/policies?page=1&pageSize=25',
  '/api/policies?status=Active&page=1&pageSize=25',
  '/api/claims?page=1&pageSize=25',
];

export function reads() {
  const path = LISTS[Math.floor(Math.random() * LISTS.length)];
  const res = http.get(`${BASE_URL}${path}`);
  check(res, { [`GET ${path} -> 200`]: (r) => r.status === 200 });
  sleep(0.2);
}

export function writes() {
  const tag = `load-${__VU}-${__ITER}-${Date.now()}`;
  const res = http.post(
    `${BASE_URL}/api/policyholders`,
    JSON.stringify({ firstName: 'Load', lastName: 'Test', email: `${tag}@example.com` }),
    { headers: H },
  );
  check(res, { 'POST holder -> 201': (r) => r.status === 201 });
  sleep(1);
}
