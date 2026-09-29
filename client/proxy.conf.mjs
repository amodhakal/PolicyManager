/**
 * Proxies `/api` and `/health` to the running API during development.
 *
 * The proxy is what lets the browser talk to the API without CORS being enabled on it. Adding a
 * CORS policy to the API would widen who may call it from any origin for the benefit of a developer
 * setup, which is the wrong trade for a service that holds encrypted personal data; a same-origin
 * proxy on the dev server costs the API nothing and is the usual arrangement for a split dev setup.
 *
 * Both paths are proxied, not just `/api`, because the health endpoints are the only unauthenticated
 * calls and the sign-in page uses one to tell "the API is down" from "this token is wrong".
 *
 * This file is read as JSON by the dev server, so it carries no comments in the body — the block
 * above is the documentation. `process.env` is substituted at read time, which is what lets
 * POLICYMANAGER_API retarget the client at a container or a deployed API without an edit.
 */
const target = process.env['POLICYMANAGER_API'] ?? 'https://localhost:7080';

export default {
  '/api': {
    target,
    secure: false,
    changeOrigin: true,
  },
  '/health': {
    target,
    secure: false,
    changeOrigin: true,
  },
};
