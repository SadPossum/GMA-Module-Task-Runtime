# TaskRuntime Production Hardening Task

Status: module implementation and verification complete; downstream alignment in progress
Date: 2026-07-19

## Goal

Make the optional TaskRuntime module production-ready for durable queued work, retries, cancellation, operator controls and long-running handlers without moving task-owning domain behavior, provider persistence or product scheduling policy into Framework.

## Ownership Boundary

- Framework owns dependency-neutral task requests, leases, execution contexts, state-transition semantics, worker orchestration, handler contracts and generic store outcomes;
- TaskRuntime owns durable run and control-message state, provider-specific concurrency, active-run deduplication, migrations, retention and operator application/API/CLI surfaces;
- Extensions own reusable bridges that understand more than one module, such as tenancy-aware execution context preparation;
- task-owning modules own payload contracts, payload-version compatibility, handlers, semantic idempotency and domain-side effects;
- products own schedules, capacity targets, operator authorization assignments, payload sensitivity rules and product workflows;
- hosts own database sizing, worker-group topology, node/worker identities, lease and timeout values, deployment draining, encryption, retention values and alert thresholds.

## Audit Baseline

- `EnqueueAsync` performs a check-then-insert and returns no durable enqueue outcome; concurrent requests can create duplicate active runs, while a sequential deduplicated enqueue can make the application query the unused requested id and report `RunNotFound`;
- generic claiming uses a serializable EF read-then-write transaction without PostgreSQL `SKIP LOCKED` or SQL Server `UPDLOCK, READPAST`, creating avoidable contention and deadlock/retry pressure as worker count grows;
- execution ownership checks compare worker and node identities but not the persisted lease generation, so a stale execution can mutate a reclaimed lease when process identities are reused;
- run transitions have no optimistic concurrency token, leaving heartbeat, timeout, cancellation, retry and completion updates vulnerable to stale read-then-write races;
- cancellation, retry and control handlers validate state with one read and mutate with another, so a concurrent transition can turn a valid application result into an exception or enqueue a control after a run becomes terminal;
- expired pending or delivered control messages are excluded from reads but are never transitioned to `Expired`, preventing the configured expired-control retention path from cleaning them;
- application enqueue validation catches malformed JSON but does not map the remaining shared-contract validation failures into stable application errors;
- list results expose a page without a total count, limiting reliable operator pagination at production history sizes;
- module-owned provider behavior is exercised from Skeleton instead of the standalone module, while the module has no tests, boundary guard, migration-drift guard, Linux lane, required relational lane or explicit transitive vulnerability audit;
- task payloads, progress, errors and actor fields can contain sensitive operational data, while retention is deliberately disabled until each product chooses a policy.

## Delivery Slices

1. Establish standalone ownership: add the task record, module-owned unit and relational test projects, reusable-module boundary checks, migration-drift checks, Windows/Linux validation, package audit and a required PostgreSQL/SQL Server relational lane.
2. Harden reusable task semantics in Framework: return explicit enqueue and mutation outcomes, add a persisted lease generation to leases/execution contexts, and expose state-machine behavior needed by any durable store without referencing TaskRuntime or a database provider.
3. Harden TaskRuntime persistence: make enqueue deduplication atomic and return the canonical run, use provider-specific skip-locked claim paths, fence worker writes by lease generation, add optimistic concurrency, and make timeout scanning race-safe.
4. Harden controls and operator mutations: atomically cancel, retry and enqueue controls; expire unread controls durably; keep retries bounded and idempotent; map invalid commands to stable application errors.
5. Make operator reads scalable: return total-count pagination, preserve deterministic ordering, add provider indexes that match supported filters, and keep stats/list payloads bounded.
6. Prove retention translation and lifecycle behavior on both providers, including expired controls, terminal runs with control history and bounded batches.
7. Document payload sensitivity, semantic idempotency, lease/timeout sizing, worker identity, shutdown/drain, capacity and retention responsibilities.
8. Move TaskRuntime-owned integration coverage out of Skeleton, publish exact Framework and TaskRuntime heads, then verify Skeleton and BunkFy against those heads.

## Non-Goals

- product task payloads, schedules, handlers, recurring-calendar semantics or business-side compensation;
- a distributed workflow engine, DAG/orchestration language or exactly-once side-effect guarantee;
- a universal worker autoscaler, multi-region leader election or external queue transport;
- product-specific scope authorization, role definitions or task visibility rules;
- storing provider credentials or introducing another reusable-module dependency;
- selecting one retention period or encrypting application databases inside the reusable module.

## Acceptance Criteria

- concurrent enqueues for one active deduplication identity produce one canonical active run on PostgreSQL and SQL Server, and every caller receives that canonical run;
- concurrent workers claim disjoint ready runs without duplicate execution and without serializing healthy queue throughput behind one global lock;
- a stale execution context cannot start, heartbeat, report progress, read or complete a later lease even when worker and node ids are reused;
- heartbeat, completion, timeout, cancel and retry races produce a valid single outcome rather than stale overwrites or unhandled concurrency exceptions;
- controls cannot be created after a run becomes terminal, expired controls become durably terminal, and configured retention can remove them;
- invalid enqueue, filter and control input returns stable application failures instead of escaping contract or provider exceptions;
- operator list results include deterministic bounded pagination and total count, with indexes supporting documented filters;
- source projects reference no other reusable module and contain no product-specific source;
- SQL Server and PostgreSQL migration models have no pending changes;
- standalone build, fast tests, boundary checks and package audit pass on Windows and Linux, and required relational tests pass for both providers;
- Skeleton and BunkFy pass against the exact published Framework and TaskRuntime heads.

## Current Evidence

- Framework is published at `017cfd7`; its build and all 972 tests pass, including lease-generation fencing, rejected worker transition behavior, bounded paging, delayed control expiry and retry-safe control delivery during concurrent heartbeats;
- standalone TaskRuntime build passes with 20 application tests, reusable-module boundary checks, SQL Server/PostgreSQL migration-drift checks and a transitive package audit with no known vulnerable packages;
- required relational tests pass against real PostgreSQL and SQL Server containers, covering concurrent canonical deduplication, case-sensitive scope parity, disjoint skip-locked claims, reused-worker lease fencing, heartbeat/timeout, completion/cancel and retry races, concurrent heartbeat/control delivery, stale control reads, durable expiry without a worker, retention and total-count pagination;
- Skeleton-owned TaskRuntime tests now cover only hosted sample execution, cooperative control and cross-module projection rebuild composition; provider persistence scenarios live in this module;
- the Skeleton build and focused worker, projection-rebuild and cooperative-control Docker tests pass against the published Framework head and the current TaskRuntime source;
- exact Skeleton and BunkFy pins, their full validation lanes and downstream CI remain the final acceptance gate for this slice.
