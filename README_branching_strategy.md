# Branching & Release Strategy — Dedicated `uat` + RC Assembly (No Flags)

> Purpose: Ship only **business‑approved** features to **UAT** and then to **Production** without cherry‑picking. We curate each UAT cycle in a short‑lived **release candidate (`rc/*`)** branch and update a permanent **`uat`** branch via PRs from that RC.

---

## Long‑lived branches

- `main` — **single source of truth**, mirrors Production. Prod deploys come **only from tags on `main`**.
- `develop` — integration branch for engineers; **Dev** environment auto‑deploys from here.
- `uat` — permanent UAT line; **UAT** environment deploys from here. **Only PRs from `rc/*` are allowed to merge into `uat`.**

## Short‑lived branches

- `feature/<ticket>-<desc>` — created from `develop`; squash‑merge back into `develop`.
- `rc/<YYYYMMDD>-<name>` — **assembly branch** for a UAT cycle (e.g., `rc/20250813-aug-sprint`).
- `hotfix/<YYYYMMDD>-<desc>` — created from the latest production **tag** for emergencies.

## Environments

- **Dev** → merges to `develop`
- **UAT** → merges to `uat` (via PRs from `rc/*`)
- **Prod** → tags on `main` (e.g., `v2025.08.0`)

---

## Naming conventions

- Feature: `feature/1234-invoice-import`
- RC: `rc/20250813-aug-sprint`
- Hotfix: `hotfix/20250818-nullref-import`
- Tags (CalVer): `vYYYY.MM.PATCH` → `v2025.08.0`, `v2025.08.1`

---

## Day‑to‑day developer workflow

1. Create feature branch off `develop`:
   ```bash
   git switch develop
   git pull --ff-only
   git switch -c feature/1234-invoice-import
   ```
2. Work, commit, push, open PR **into `develop`**. Keep branch current:
   ```bash
   git fetch
   git rebase origin/develop
   ```
3. When CI is green and reviewed, **squash‑merge** to `develop`.  
   Merge to `develop` → **auto‑deploys to Dev**.

---

## Getting a feature **eligible for UAT**

A feature can be considered for UAT **only if**:

- ✅ Merged into `develop` (squash) and **CI green**.
- ✅ Any DB migrations are **forward‑only** and verified on Dev.
- ✅ The feature branch head is **rebased on latest `develop`** (if still active).
- ✅ Business has marked it **approved for UAT** (e.g., PR label `approved-for-uat`).

---

## Start a UAT cycle (reset baseline)

Before curating, make sure `uat` equals `main`:

```bash
git fetch origin
git switch uat
git merge --ff-only origin/main
git push
```

Cut a **new RC** from `uat` (which now matches `main`):

```bash
git switch --detach origin/uat
git switch -c rc/20250813-aug-sprint
git push -u origin rc/20250813-aug-sprint
```

Pushing `rc/*` should **auto‑deploy UAT preview** (empty baseline) & run UAT test suite.

---

## Curate business‑approved features into RC (no cherry‑picks)

Merge **whole feature branches** into the RC:

```bash
git switch rc/20250813-aug-sprint
git fetch origin

# For each approved feature:
git merge --no-ff origin/feature/1234-invoice-import
git merge --no-ff origin/feature/1299-bulk-pricing
# Resolve conflicts once (here), then:
git commit   # only if conflict resolution created a pending merge
git push
```

> If a feature branch was deleted after squashing: recreate a release head from the merge SHA on `develop` and merge that into RC.

Each push to `rc/*` updates the RC and should build/test artifacts for UAT.

---

## Promote RC → UAT via PR

1. Open PR **from `rc/<...>` to `uat`**.
2. CI for the PR runs UAT‑grade tests (E2E, smoke, seeded data).
3. Merge PR → **auto‑deploy to UAT**.

Repeat as needed during the cycle—continue merging approved features into RC and creating additional PRs from the RC to `uat`.

**Guardrail:** Only allow PRs from `rc/*` into `uat`. No direct merges from `develop` or `feature/*` into `uat`.

---

## Release to Production

When UAT is fully signed off:

```bash
# Merge what’s in UAT into main
git switch main
git pull --ff-only
git merge --no-ff origin/uat
git push

# Tag & deploy Prod
git tag v2025.08.0
git push origin v2025.08.0
```

Sync `develop` with what shipped and clean up the RC:

```bash
git switch develop
git merge --ff-only origin/main
git push

git push origin --delete rc/20250813-aug-sprint
```

---

## Hotfixes (prod‑first, then backflow)

```bash
# 1) Branch from production tag
git fetch --tags
git switch -c hotfix/20250818-nullref v2025.08.0

# 2) Fix → PR → main → tag & deploy
git tag v2025.08.1
git push origin v2025.08.1

# 3) Back‑merge to active lines
# UAT: fast‑forward from main
git switch uat
git merge --ff-only origin/main
git push

# RC (if active): include hotfix so future UAT batches have it
git switch rc/20250813-aug-sprint
git merge --no-ff hotfix/20250818-nullref
git push

# develop
git switch develop
git merge --ff-only origin/main
git push
```

---

## CI/CD reference

- **PR → `develop`**: build, unit/integration tests, lint, SAST (no deploy).
- **Merge → `develop`**: publish artifact/image, **auto‑deploy Dev**.
- **PR `rc/*` → `uat`**: promote known artifact (or rebuild deterministically), run E2E + smoke + data seeds, approvals required.
- **Merge to `uat`**: **auto‑deploy UAT**.
- **Tag `v*` on `main`**: promote immutable artifact, supply‑chain signing (SBOM), approvals, **deploy Prod**.

> Tip: Promote the **same artifact digest** Dev → UAT → Prod for reproducibility.

---

## Branch protection rules (recommended)

- `main`
  - PRs only, **2 approvals**, CI green, **signed tags required**, no force‑push.
- `develop`
  - PRs only, **squash merges**, CI green, no force‑push.
- `uat`
  - PRs only, **source branch must match `rc/*`**, restricted maintainers, CI green.
- `rc/*`
  - Restricted to Release Managers; merges require CI green.

Add **CODEOWNERS** for DB/infra to require owners’ approvals when touched.

---

## Conflict & rollback policy

- Before merging a feature into RC, it must be **rebased on latest `develop`** and CI‑green.
- Conflicts are resolved **once in RC** (not repeatedly in `uat`).
- If a feature fails in UAT, **revert its merge commit in `uat`** and in the RC to keep them aligned:
  ```bash
  git switch uat
  git log --merges
  git revert -m 1 <merge_commit_sha_of_feature>
  git push
  ```

---

## Database & config guidance

- Prefer **forward‑only migrations**. Run migrations in UAT pipeline; block Prod unless “migrations OK” passed in UAT.
- For destructive changes, use **expand → migrate → contract** across releases.

---

## Automation suggestions (optional)

- Label `approved-for-uat` on the feature PR triggers a bot to open/update a PR from `feature/*` → active `rc/*`.
- A “Ready for RC” check verifies:
  - Rebased on `develop`
  - CI green
  - Optional: no TODO markers

---

## FAQ

**Why not cherry‑pick from `develop` to UAT?**  
Cherry‑picks fragment history and increase conflict risk. Merging **whole feature branches** keeps changes atomic and auditable.

**Can we run multiple RCs?**  
Avoid it. Keep **one active RC** to reduce complexity. Finish or close it before starting the next.

**How do we exclude a failing feature late in the cycle?**  
Revert its merge commit in RC and `uat` (`git revert -m 1 <sha>`), keep the rest.

---

## Example end‑to‑end

1) Features `#1234`, `#1299` merged to `develop`, labeled `approved-for-uat`.  
2) Reset `uat` = `main`, cut `rc/20250813-aug-sprint` from `uat`.  
3) Merge approved `feature/*` into RC; open PR **RC → `uat`**; UAT deploys.  
4) UAT passes; merge `uat` → `main`, tag `v2025.08.0`, deploy Prod.  
5) Merge `main` → `develop`; delete RC.

---

**Owner:** Release Management  
**Last updated:** 2025‑08‑13
