# Kubernetes (local only)

Full-stack demo on a **local** cluster (Docker Desktop Kubernetes or kind).
No cloud, no registry, no TLS. Cloud topology is deliberately out of scope.

What runs where:

| Workload | Kind | Notes |
|---|---|---|
| `api` | Deployment (2 replicas) + HPA 2–6, PDB | `policymanager:local`, probes on `/health` vs `/health/ready` |
| `api-migrate` | Job (once per deploy) | Runs `/app/migrate` bundle, retries 30×2s |
| `mssql` | StatefulSet ×1 + 10Gi PVC | Demo only. Prod = managed DB (Azure SQL/RDS) |
| `rabbitmq` | Deployment ×1 | Broker opt-in; `Broker__Enabled=true` by default here |
| `client` | Deployment ×1 | nginx proxies `/api`, `/health` → `http://api:8080` |
| `ingress` | Optional | Needs ingress-nginx; port-forward works without it |

`IMemoryCache` is per-pod, so 2 API replicas can disagree briefly on a
single-holder read. That is accepted for a local demo and documented so it
is an interview answer, not a surprise. Prod fix: Redis / sticky sessions.

## Prerequisites

- Docker Desktop with Kubernetes enabled (or kind), `kubectl`, `openssl`.
- `k8s/secrets.yaml` is already filled with **local-dev-only** values.
  Rotate with `./scripts/generate-k8s-secrets.sh`.

## Build (no registry)

```bash
docker build -f PolicyManager/Dockerfile -t policymanager:local .
docker build -f client/Dockerfile -t policymanager-client:local ./client

# kind only: load images into the cluster
# kind load docker-image policymanager:local policymanager-client:local
```

`imagePullPolicy: IfNotPresent` is set so local images are used as-is.

## Deploy

```bash
kubectl apply -k k8s/
kubectl wait --for=condition=ready pod -l app.kubernetes.io/name=mssql -n policymanager --timeout=300s
kubectl wait --for=condition=complete job/api-migrate -n policymanager --timeout=300s
kubectl rollout status deploy/api -n policymanager
kubectl get pods,hpa,pdb,ingress -n policymanager
```

Order matters: namespace/secrets/config → StatefulSet → Job → Deployments.
`kubectl apply -k` applies all at once; the waits above serialize readiness.

## Use (no ingress needed)

```bash
kubectl port-forward svc/api 8080:8080 -n policymanager &
curl -s http://localhost:8080/health | head -c 300; echo
curl -s http://localhost:8080/health/ready | head -c 300; echo

kubectl port-forward svc/client 4200:80 -n policymanager &
open http://localhost:4200
```

With ingress-nginx + `127.0.0.1 policymanager.local` in `/etc/hosts`:
`http://policymanager.local/` (client), `/api/*` and `/health*` (api).

## Rolling deploy demo (resume metric)

```bash
# Terminal 1: steady load (needs a valid JWT; see client sign-in flow)
# k6 or: while true; do curl -sf http://localhost:8080/health/ready > /dev/null || echo FAIL; sleep 0.2; done

# Terminal 2:
kubectl rollout restart deploy/api -n policymanager
kubectl rollout status deploy/api -n policymanager
```

Expected: `maxUnavailable: 0` + readiness gate = 0 failed `ready` probes,
PDB keeps ≥1 pod. Record: requests, failures, `kubectl get hpa`,
`kubectl describe pod` restart counts. That is the number for the resume —
measure it, do not invent it.

Suggested bullet once measured:

> Containerized ASP.NET Core + Angular + SQL Server/RabbitMQ for local
> Kubernetes (Kustomize, `kubectl apply -k`); wired `/health` vs
> `/health/ready` to liveness/readiness/startup probes with EF-migration Job
> and RollingUpdate (`maxUnavailable: 0`) + PDB + HPA 2–6 — N rolling
> restarts under X RPS with 0 failed readiness probes (k6 + kubectl).

## Tear down

```bash
kubectl delete -k k8s/
# kind: kind delete cluster
```

PVCs are deleted with the StatefulSet via `volumeClaimTemplates` only when
the StatefulSet is deleted with `--cascade`; check `kubectl get pvc -n policymanager`.
