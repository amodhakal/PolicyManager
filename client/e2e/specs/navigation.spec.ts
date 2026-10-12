import { expect } from '@wdio/globals';

import { mintToken } from '../helpers/token';
import { seedToken } from '../helpers/auth';

const signingKey = process.env['E2E_JWT_SIGNING_KEY'] ?? 'e2e-test-signing-key';

describe('Navigation', () => {
  beforeEach(async () => {
    const token = await mintToken(['Admin'], signingKey);
    await seedToken(token);
    await browser.url('/');
  });

  it('shows all nav links for Admin', async () => {
    const nav = await $('[data-testid="main-nav"]');
    await nav.waitForDisplayed();
    const links = await nav.$$('a');
    const texts = await Promise.all(links.map((l) => l.getText()));
    expect(texts).toContain('Policyholders');
    expect(texts).toContain('Policies');
    expect(texts).toContain('Claims');
    expect(texts).toContain('Reports');
  });

  it('displays the signed-in identity', async () => {
    const identity = await $('[data-testid="identity"]');
    await identity.waitForDisplayed();
    expect(await identity.getText()).toBeTruthy();
  });

  it('signs out and redirects to sign-in', async () => {
    const signOutBtn = await $('[data-testid="sign-out-button"]');
    await signOutBtn.waitForDisplayed();
    await signOutBtn.click();

    await browser.waitUntil(
      async () => (await browser.getUrl()).toContain('/sign-in'),
      { timeout: 10000 },
    );

    const nav = await $('[data-testid="main-nav"]');
    await expect(await nav.isExisting()).toBe(false);
  });

  it('hides the New policy button for Agent role', async () => {
    const agentToken = await mintToken(['Agent'], signingKey);
    await seedToken(agentToken);
    await browser.url('/policies');

    const table = await $('[data-testid="policy-table"]');
    await table.waitForDisplayed({ timeout: 10000 });

    const newBtn = await $('[data-testid="new-policy-button"]');
    await newBtn.waitForDisplayed();
    await expect(await newBtn.getAttribute('title')).toContain('Creating a policy needs');
  });
});
