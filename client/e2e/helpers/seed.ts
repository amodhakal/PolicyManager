const API_BASE = process.env['E2E_API_BASE'] ?? 'http://localhost:8080/api';

const signingKey = process.env['E2E_JWT_SIGNING_KEY'] ?? 'e2e-test-signing-key';

async function apiFetch<T>(
  path: string,
  token: string,
  options: RequestInit = {},
): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      ...options.headers,
    },
  });
  if (!response.ok) {
    throw new Error(`API ${response.status} ${response.statusText} for ${path}`);
  }
  if (response.status === 204) {
    return undefined as T;
  }
  return response.json() as Promise<T>;
}

export async function seedAll(): Promise<void> {
  const { mintToken } = await import('./token');
  const adminToken = await mintToken(['Admin'], signingKey);

  const holder = await apiFetch<{
    id: number;
    firstName: string;
    lastName: string;
  }>('/policyholders', adminToken, {
    method: 'POST',
    body: JSON.stringify({
      firstName: 'E2E',
      lastName: 'Holder',
      email: 'e2e.holder@example.com',
    }),
  });

  await apiFetch('/policies', adminToken, {
    method: 'POST',
    body: JSON.stringify({
      policyHolderId: holder.id,
      type: 'Auto',
      premium: 1200.0,
      startDate: '2026-01-01',
      endDate: '2027-01-01',
    }),
  });

  await apiFetch('/claims', adminToken, {
    method: 'POST',
    body: JSON.stringify({
      policyId: holder.id,
      amount: 500.0,
      description: 'E2E seeded claim',
    }),
  });
}
