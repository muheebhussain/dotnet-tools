# Database Branching & Migration Strategy (SSDT + SQL Server)

## Purpose
This document explains how we:
- Migrate a **legacy SQL Server database** to a **standardized schema**
- Do it **feature by feature**
- Allow **multiple developers** to work safely in parallel
- Support **monthly / bi-monthly releases**
- Meet **regulatory and audit requirements**

This strategy is optimized for **Visual Studio Database Projects (SSDT)**.

---

## Key Concepts (Plain English)

### 1. One Source of Truth
- The **database project in Git** is the source of truth.
- Manual changes to DEV / UAT / PROD are not allowed.
- All schema changes must come from Git → CI/CD → DACPAC publish.

---

### 2. Trunk-Based Development (with Release Branches)
We use a **hybrid trunk-based strategy**:

- `main` = integration branch (future work)
- `release/*` = frozen, auditable release branch
- `feature/*` = short-lived work branches
- `hotfix/*` = emergency fixes during release window

This minimizes merge conflicts and keeps refactors safe.

---

## Branching Model

### Branch Types

| Branch | Purpose |
|------|--------|
| `main` | Active development for upcoming releases |
| `feature/*` | Feature-by-feature migration work |
| `release/YYYY.MM.R#` | Frozen release candidate (UAT & PROD) |
| `hotfix/*` | Approved fixes during release |

### Examples
main
feature/mig-inventory-receiving
feature/mig-pricing-standardization
release/2026.01.R1
release/2026.01.R2
hotfix/INC-1234-fix-index


---

## Environment Mapping

| Environment | Deploys From |
|-----------|-------------|
| DEV | `main` |
| UAT | `release/*` |
| PROD | Tag from `release/*` |

**Important rule:**  
UAT and PROD always come from the same release branch lineage.

---

## Feature-by-Feature Migration (How We Work)

### Example Feature: Inventory Receiving

#### Step 1: Create a feature branch

git checkout main
git pull
git checkout -b feature/mig-inventory-receiving


#### Step 2: Make schema changes in SSDT
- Rename tables/columns using **SSDT refactor** (not manual edits)
- Add new standardized tables/procs
- Add compatibility views or wrapper procs if needed
- Add post-deploy scripts if data transformation is required

Example:
- `InvRecvTbl` → `InventoryReceiving`
- `RecvQty` → `ReceivedQuantity`
- Add view `InvRecvTbl` for backward compatibility (temporary)

#### Step 3: Commit & PR to `main`
- Keep PR small and focused
- CI must:
  - Build DACPAC
  - Validate references
  - Deploy to DEV / test DB

After merge:
- DEV is updated from `main`
- Feature is officially part of the next release

---

## Release Process (Monthly / Bi-Monthly)

### 1. Cut a Release Branch
When ready to freeze a release:

git checkout main
git pull
git checkout -b release/2026.01.R1


From this point:
- Only **approved changes** go into `release/2026.01.R1`
- New features continue on `main`

Deploy `release/2026.01.R1` to **UAT**.

---

### 2. Fixing Issues in UAT
If a defect is found:

git checkout release/2026.01.R1
git checkout -b hotfix/INC-1234-fix-missing-index


- Fix the issue
- PR into `release/2026.01.R1`
- After merge → **cherry-pick the same commit into `main`**

This prevents divergence.

---

### 3. Production Release
Once UAT is approved:

1. Tag the release commit:
   
prod/2026.01.R1


2. Build DACPAC and publish script from the **tag**
3. Deploy to PROD
4. Archive artifacts:
   - DACPAC
   - Publish.sql
   - Publish report
   - Commit hash + approvals

This satisfies audit requirements.

---

## SSDT Rules (Very Important)

### Refactors (Renames)
- Always use **SSDT refactor tools**
- Keep refactor changes **small**
- Merge quickly to avoid refactor log conflicts
- Never refactor the same object on multiple branches

### Drops & Destructive Changes
- Avoid dropping tables/columns during migration
- Use a “deprecate first, drop later” approach
- Drops happen in **dedicated cleanup features**

### Data Transformations
Use:
- `Pre-Deploy` scripts → prepare data
- `Post-Deploy` scripts → backfill, cleanup

Never rely on SSDT auto-drop logic for complex data changes.

---

## CI/CD Expectations

Every PR:
- Builds database project
- Validates unresolved references
- Deploys to a disposable or DEV database
- Generates publish script

Every release:
- Uses a **tagged commit**
- Produces immutable artifacts
- Requires approvals

---

## Why This Strategy Works

- Minimizes merge conflicts
- Keeps SSDT refactors safe
- Supports parallel development
- Maintains auditability
- Enables continuous migration without release risk

---

## Summary (TL;DR)

- `main` = active integration
- `feature/*` = short-lived, focused changes
- `release/*` = frozen, auditable releases
- DEV ← `main`
- UAT ← `release/*`
- PROD ← tagged release
- Refactor carefully, merge often, release safely

---

For questions or exceptions, contact the DB Technical Lead.
