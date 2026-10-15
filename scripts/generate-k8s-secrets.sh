#!/bin/sh
# Regenerates k8s/secrets.yaml with fresh LOCAL-DEV-ONLY values.
# Usage: ./scripts/generate-k8s-secrets.sh
# Never run against a shared or production cluster — use ExternalSecrets there.
set -eu

OUT="k8s/secrets.yaml"

DB_PASSWORD="DevStrong!Passw0rd$(openssl rand -hex 3)"
JWT_KEY="$(openssl rand -base64 48)"
PII_ENC="$(openssl rand -base64 32)"
PII_BLIND="local-dev-blind-index-$(openssl rand -hex 16)-for-hmac"

cat > "$OUT" <<EOF
# LOCAL DEV ONLY — do not use in any shared, staging, or production cluster.
# These values are committed so \`kubectl apply -k k8s/\` works out of the box
# on a local cluster (Docker Desktop / kind). They protect nothing.
# For anything beyond local: use ExternalSecrets / SealedSecrets and rotate.
# Regenerate with: ./scripts/generate-k8s-secrets.sh
apiVersion: v1
kind: Secret
metadata:
  name: policymanager-secrets
  namespace: policymanager
  labels:
    app.kubernetes.io/name: policymanager
    app.kubernetes.io/part-of: policymanager
type: Opaque
stringData:
  # Must satisfy the SQL Server password policy: >= 8 chars and 3 of
  # upper / lower / digit / symbol. Matches compose.yaml DB_PASSWORD semantics.
  DB_PASSWORD: '${DB_PASSWORD}'
  # HMAC-SHA256 signing key. Must be >= 32 bytes. Generate with: openssl rand -base64 48
  Jwt__SigningKey: '${JWT_KEY}'
  # AES-256-GCM key. Must decode to exactly 32 bytes. Generate with: openssl rand -base64 32
  Pii__EncryptionKey: '${PII_ENC}'
  # Blind-index HMAC key. Must be >= 32 bytes.
  Pii__BlindIndexKey: '${PII_BLIND}'
EOF

echo "Wrote $OUT (local dev values only)."
