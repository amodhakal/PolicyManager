const TOKEN_KEY = 'policymanager.token';

export async function seedToken(token: string): Promise<void> {
  await browser.execute((key, value) => {
    localStorage.setItem(key, value);
  }, TOKEN_KEY, token);
}

export async function clearToken(): Promise<void> {
  await browser.execute((key) => {
    localStorage.removeItem(key);
  }, TOKEN_KEY);
}

export async function signInWithToken(token: string): Promise<void> {
  await browser.url('/sign-in');
  const textarea = await $('textarea[name="token"]');
  await textarea.waitForDisplayed();
  await textarea.setValue(token);
  await $('button[type="submit"]').click();
}
