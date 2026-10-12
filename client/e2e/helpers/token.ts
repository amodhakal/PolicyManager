const encoder = new TextEncoder();

function base64UrlEncode(data: Uint8Array): string {
  let binary = '';
  for (const byte of data) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

async function hmacSha256(key: string, data: string): Promise<Uint8Array> {
  const cryptoKey = await crypto.subtle.importKey(
    'raw',
    encoder.encode(key),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign'],
  );
  const signature = await crypto.subtle.sign('HMAC', cryptoKey, encoder.encode(data));
  return new Uint8Array(signature);
}

export async function mintToken(
  roles: string[],
  signingKey: string,
  expiresInSeconds = 3600,
): Promise<string> {
  const header = { alg: 'HS256', typ: 'JWT' };
  const now = Math.floor(Date.now() / 1000);
  const payload = {
    iss: 'policy-manager',
    aud: 'policy-manager-api',
    sub: 'e2e-test-user',
    exp: now + expiresInSeconds,
    iat: now,
    'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': roles,
  };

  const headerSegment = base64UrlEncode(encoder.encode(JSON.stringify(header)));
  const payloadSegment = base64UrlEncode(encoder.encode(JSON.stringify(payload)));
  const signature = await hmacSha256(signingKey, `${headerSegment}.${payloadSegment}`);
  const signatureSegment = base64UrlEncode(signature);

  return `${headerSegment}.${payloadSegment}.${signatureSegment}`;
}
