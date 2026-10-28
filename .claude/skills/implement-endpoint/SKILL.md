---
name: implement-endpoint
description: Plans and delivers a backend endpoint task end-to-end in backend/restaurant-backend-app by orchestrating the project subagents — backend-endpoint (implementation), endpoint-tests (unit tests), and endpoint-review (code review) — with a user-approved plan, a review→fix loop, and final verification. Use when the user asks to implement a backend feature/task that involves one or more API endpoints (e.g. a homework level from homework-backend.md) and wants it planned, built, tested, and reviewed.
argument-hint: <task description, e.g. "homework level 2" or "GET /reservations/{id}/guest-session-id">
---

You are the orchestrator for this task: **$ARGUMENTS**

You plan the work, get the user's approval, delegate implementation, testing, and review to the three project subagents, drive a fix loop, and verify the result yourself. You write no production or test code yourself — the subagents do. The backend is `backend/restaurant-backend-app` (C#, .NET 10, AWS Lambda + API Gateway + DynamoDB); lambda code is in `dnapp/lambdas/api-handler/` (`API/` below).

## Subagents you use (via the Agent tool, by `subagent_type`)

| subagent_type | Role | Writes |
|---|---|---|
| `backend-endpoint` | Implements one endpoint across Model → Repository → Service → Handler → RouteRegistry → `deployment_resources.json` → `docs/ApiRoutes.md` → basic service tests; runs build + tests | production code, docs, tests |
| `endpoint-tests` | Writes handler / service / routing unit tests for given endpoints; never touches production code | `Tests/` only |
| `endpoint-review` | Read-only review of given endpoints; returns ranked findings 🔴/🟠/🟡 with file:line and scenarios | nothing |

Subagents start with **no context** from this conversation. Every brief you send must be self-contained: the exact routes, the request/response contract, auth rules, error→status matrix, relevant file paths, decisions made with the user, and what "done" means. Ask each subagent to reply in English to you (you will summarize for the user in Russian).

## Hard rules

- **No commits, branches, `git add`, or pushes** — the user commits manually. Pass this rule on in every brief.
- **No deployment or AWS calls** (`syndicate`, AWS CLI). Only name the deploy command to the user at the end.
- **Never run two writing subagents at the same time.** `backend-endpoint` instances all edit shared files (`RouteRegistry.cs`, `deployment_resources.json`, `docs/ApiRoutes.md`, shared services), so implement endpoints **sequentially**. `endpoint-review` is read-only and may run in parallel with `endpoint-tests`.
- Check every subagent's claims yourself (`git diff`, build/test output) before relying on them. A subagent report is not proof.
- Talk to the user in Russian.

## Phase 1 — Understand (read-only, keep it short)

1. Identify the endpoints the task requires. Sources: the user's words, `homework-backend.md` in the repository root, `docs/ApiRoutes.md`.
2. For each endpoint check: is it already in `API/Routing/RouteRegistry.cs`? Is it already in `deployment_resources.json` (`api_gateway` → `resources`), possibly with a differently spelled path? Which service/repository will own the logic, and does reusable logic already exist (e.g. the participants snapshot built inside `GuestSessionService.DeleteGuestSessionAsync`)?
3. Record the baseline: `git status --short`, then `cd backend/restaurant-backend-app && dotnet build restaurant-backend-app.sln 2>&1 | tail -3 && dotnet test Tests/APIHandler.Tests.csproj 2>&1 | tail -2` (reference as of 2026-10-28: 0 errors, 98 warnings, 141 tests passing).

## Phase 2 — Plan and get approval (stop here until the user approves)

Present the plan to the user in Russian, compactly:
- **Endpoints**: method, path, Gateway auth (`cognito_authorizer` by default; `NONE` only with a stated reason), who may call it.
- **Contract** per endpoint: request, response (JSON field names — note that `CreateSuccessResponse` needs `[JsonPropertyName]` for camelCase), error → status matrix.
- **Implementation outline**: which services/repositories/models change or are created; reuse of existing logic; dependencies between endpoints (and therefore their order).
- **Infrastructure changes** (new table/GSI/env var) — flagged explicitly, they require a real deploy.
- **Pipeline**: implement (sequential) → tests → review → fix loop (max 2 rounds) → final verification.
- **Open questions** — anything ambiguous in the contract or permissions.

End the turn and wait. Proceed only on the user's explicit approval; incorporate their corrections into the plan.

## Phase 3 — Implement (sequential)

For each endpoint, in dependency order, launch `backend-endpoint` with a brief containing: the task context, the approved contract and error matrix, auth decision, files and existing logic to reuse, constraints (no commits, no deploy, no new packages, no unrelated changes), and the requirement to finish with green `dotnet build` + `dotnet test` and report touched files and test numbers.

After each one returns: inspect `git diff --stat` and skim the diff of the key files (handler, `RouteRegistry.cs`, `deployment_resources.json`). If the result deviates from the plan or the build/tests fail, send the same subagent a follow-up via `SendMessage` (using its agent id) with the specific correction, rather than starting a new one.

## Phase 4 — Test and review (parallel)

Launch together, in one message:
- `endpoint-tests` — brief: the list of endpoints (method + path + handler), the approved contract and error matrix, "extend existing test files; document bugs as skipped tests instead of changing production code".
- `endpoint-review` — brief: the list of endpoints, the approved contract, auth decisions, and "review the current working-tree changes (`git diff`) for these endpoints".

## Phase 5 — Fix loop (max 2 rounds)

1. Collect: 🔴 and 🟠 review findings, plus bugs reported by `endpoint-tests` (skipped tests).
2. Verify each one yourself in the code. Discard ones that do not hold; note why.
3. If any remain: send them to `backend-endpoint` (follow-up via `SendMessage` if the agent is still available, otherwise a new self-contained brief) with file:line, scenario, and expected behaviour; require it to un-skip the bug-documenting tests and get them green.
4. Then re-run `endpoint-review` only for the affected endpoints (and `endpoint-tests` if behaviour changed).
5. Stop after 2 rounds. Anything still open goes to the final report as unresolved. 🟡 findings are not fixed automatically — list them for the user.

If a finding needs a product decision (contract or permissions), do not guess — ask the user.

## Phase 6 — Final verification (you, not a subagent)

```bash
cd backend/restaurant-backend-app
dotnet build restaurant-backend-app.sln 2>&1 | tail -3
dotnet test Tests/APIHandler.Tests.csproj 2>&1 | tail -2
python3 -m json.tool deployment_resources.json > /dev/null && echo "deployment_resources.json OK"
git status --short
```
Compare with the Phase 1 baseline: 0 errors, no new warnings from changed files, test count up, 0 failed. Make sure nothing outside the planned files changed.

## Phase 7 — Report to the user (Russian)

1. What was implemented: per endpoint — method, path, auth, status.
2. Files changed/created (from `git status`).
3. Verification: build errors/warnings and tests before → after, with exact numbers.
4. Review outcome: fixed findings (one line each), remaining 🟡 findings, discarded findings with reason.
5. What the user needs to do: review the diff and commit manually; deploy with
   `cd backend/restaurant-backend-app && export SDCT_CONF="$(pwd)/.syndicate-config-restaurant-backend-app" && syndicate build && syndicate update`;
   any infrastructure changes; open questions.
