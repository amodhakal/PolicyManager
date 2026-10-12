import { expect } from '@wdio/globals';

import { mintToken } from '../helpers/token';
import { clearToken, signInWithToken } from '../helpers/auth';

const signingKey = process.env['E2E_JWT_SIGNING_KEY'] ?? 'e2e-test-signing-key';

describe('Authentication', () => {
  beforeEach(async () => {
    await clearToken();
  });

  it('redirects to sign-in when not authenticated', async () => {
    await browser.url('/policies');
    await expect(await browser.getUrl()).toContain('/sign-in');
  });

  it('signs in with a valid token and redirects to policyholders', async () => {
    const token = await mintToken(['Admin'], signingKey);
    await signInWithToken(token);

    await browser.waitUntil(
      async () => (await browser.getUrl()).includes('/policyholders'),
      { timeout: 10000, timeoutMsg: 'Did not redirect to /policyholders' },
    );

    await expect(await $('[data-testid="main-nav"]')).toBeDisplayed();
  });

  it('rejects an invalid token', async () => {
    await browser.url('/sign-in');

    const textarea = await $('[data-testid="token-input"]');
    await textarea.waitForDisplayed();
    await textarea.setValue('not-a-valid-token');
    await $('[data-testid="sign-in-submit"]').click();

    const rejection = await $('[data-testid="rejection-message"]');
    await rejection.waitForDisplayed();
    await expect(await rejection.getText()).toContain('not a readable JWT');
  });

  it('rejects an expired token', async () => {
    const token = await mintToken(['Admin'], signingKey, -3600);
    await browser.url('/sign-in');

    const textarea = await $('[data-testid="token-input"]');
    await textarea.waitForDisplayed();
    await textarea.setValue(token);
    await $('[data-testid="sign-in-submit"]').click();

    const rejection = await $('[data-testid="rejection-message"]');
    await rejection.waitForDisplayed();
    await expect(await rejection.getText()).toContain('expired');
  });

  it('rejects a token with no recognised roles', async () => {
    const token = await mintToken(['UnknownRole'], signingKey);
    await browser.url('/sign-in');

    const textarea = await $('[data-testid="token-input"]');
    await textarea.waitForDisplayed();
    await textarea.setValue(token);
    await $('[data-testid="sign-in-submit"]').click();

    const rejection = await $('[data-testid="rejection-message"]');
    await rejection.waitForDisplayed();
    await expect(await rejection.getText()).toContain('none of the roles');
  });
});
