# Task Runtime Scope Lifecycle

Status: complete
Date: 2026-08-05

## Goal

Give any product using Task Runtime a reusable, exact, bounded way to stop new
work for one scope, drain active leases, and remove that scope's durable task
copies without introducing product policy or orchestration into GMA.

## Ownership Boundary

- Framework Tasks owns generic run/control contracts, state transitions, worker
  execution, and store outcomes.
- Task Runtime owns durable scoped run and control-message state, mutation and
  claim admission, scope closure, bounded destruction, and immutable proof.
- Task-owning modules remain authoritative for payload meaning and business
  data. Opaque task payloads, progress, and errors are operational copies and
  are deliberately not exposed as a portability export surface.
- Products own the mapping from a product tenant to `ScopeId`, lifecycle policy,
  dependency order, retention/legal decisions, and proof interpretation.
- A product must execute final Task Runtime scope cleanup outside the target
  scope. A tenant-scoped task cannot safely delete its own durable run before
  the worker records completion.

## Slice

1. [x] Add a Contracts-owned scope snapshot and bounded destroy facade.
2. [x] Persist closing state, resumable operation progress, and an immutable
   payload-free receipt for exact replay.
3. [x] Reject new scoped enqueue, retry, and control-message writes after closure
   starts; exclude closing scopes from claims while allowing leased work to
   heartbeat, complete, cancel, or time out.
4. [x] Cancel unleased work, request cancellation of active leases, wait until all
   execution is terminal, then remove control messages and runs in bounded
   stages.
5. [x] Keep ordinary retention away from closing scopes so lifecycle proof owns the
   final deletion sequence.
6. [x] Prove provider migrations, admission races, exact replay/conflict, restart-safe
   progress, active-lease draining, and no cross-scope deletion.

The retained lifecycle proof is also enforced below the domain layer. Active
operation and terminal receipt rows are bound to the scope state, persisted
count/proof/timestamp shapes match their constructors, and the closing/closed
state coordinates are constrained. Provider-native triggers reject reopening
or deleting a closed state and changing or deleting a completion receipt. Both
trigger tables are declared in the EF model so SQL Server uses trigger-compatible
DML for state transitions and receipt inserts.

## Acceptance

- missing and existing scopes close with exact selected/resulting revisions;
- one scope closes without blocking or deleting another scope;
- no new run, retry, control message, or lease can enter after closure starts;
- already leased work can become terminal but prevents premature completion;
- every invocation performs at most one non-empty bounded batch;
- retries preserve operation id, batch size, progress, and removal proof;
- exact replay returns the immutable receipt and conflicting reuse fails closed;
- progress, receipts, logs, and public results contain no task payload, error,
  actor, or record identity; and
- PostgreSQL and SQL Server migrations remain drift-free.

## Evidence

- all 27 Task Runtime application and lifecycle tests pass;
- all 1,096 fast GMA Framework tests pass, including the Tasks infrastructure
  and shared/exclusive transaction-key-lock coverage;
- the standalone module and both provider migration projects build with zero
  warnings, and both EF migration models are drift-free;
- the exact PostgreSQL upgrade scenario proves control-scope backfill, orphan
  cleanup, concurrent scoped deduplication, shared-lock admission, claim
  fencing, active-lease drain, one-batch progress, cross-scope preservation,
  immutable replay/conflict, append-only receipt protection, and terminal state
  immutability; and
- the focused SQL Server lifecycle scenario applies the hardening migration and
  persists an empty-scope closing state, terminal transition, operation removal,
  and trigger-backed completion receipt in one call.

## Deferred

- product-specific export or retention policy for task payload contents;
- workflow/DAG orchestration or cross-scope dependency semantics; and
- BunkFy terminal orchestration that invokes this owner outside the target
  workspace task scope.
