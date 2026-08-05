# TaskRuntime Module

Current engineering work is tracked in [TaskRuntime Production Hardening Task](task-runtime-production-hardening-task.md).
Scope teardown work is tracked in [TaskRuntime Scope Lifecycle Task](task-runtime-scope-lifecycle-task.md).

The `TaskRuntime` module is an optional persisted runtime for queued tasks, long-running task handlers, progress reporting, retries, cancellation, and operator control. It is reusable infrastructure, not an example module and not a scheduler framework by itself.

## Projects

```text
Gma.Modules.TaskRuntime.Contracts
Gma.Modules.TaskRuntime.Application
Gma.Modules.TaskRuntime.Persistence
Gma.Modules.TaskRuntime.Persistence.SqlServerMigrations
Gma.Modules.TaskRuntime.Persistence.PostgreSqlMigrations
Gma.Modules.TaskRuntime.Admin.Contracts
Gma.Modules.TaskRuntime.AdminCli
Gma.Modules.TaskRuntime.AdminApi
```

`TaskRuntime` is not registered in the default `Host.Api`, `Host.AdminCli`, or `Host.AdminApi`. Applications compose it explicitly when they want persisted task runs or admin task controls.

## Responsibilities

The module owns:

- persisted task runs;
- persisted control messages;
- run enqueue/list/get/stats/cancel/retry/control use cases;
- SQL Server and PostgreSQL migrations for the `tasks` schema;
- admin CLI and admin API front doors for task operations.

The shared task contracts and worker loop live outside the module in `Gma.Framework.Tasks`, `Gma.Framework.Tasks.Infrastructure`, and optional bridge packages. Task-owning modules still own their payload contracts and handlers.

## Composition

Compose the runtime store only in hosts that need task persistence or task admin operations:

```csharp
builder.Services.AddTaskRuntimeApplication();
builder.AddTaskRuntimePersistence();
```

Compose the worker loop only in hosts that should execute queued work:

```csharp
builder.AddTaskWorkerRuntime();
```

If task handlers dispatch commands, also compose the CQRS bridge:

```csharp
builder.AddTaskCqrs();
```

For scope-aware task payloads, compose the tenancy task bridge:

```csharp
builder.AddTenantTaskExecutionContext();
```

`TaskRuntimeProfiles.Default` is selected by `Gma.Modules.TaskRuntime.AdminCli` and `Gma.Modules.TaskRuntime.AdminApi`. The profile requires the persisted run store, runtime reporter, and control channel provided by `Gma.Modules.TaskRuntime.Persistence`.

## Admin CLI

`Gma.Modules.TaskRuntime.AdminCli` contributes the `tasks` command surface when explicitly registered by an admin CLI host:

```text
tasks runs list
tasks runs list --status retry-scheduled
tasks runs stats
tasks runs get --run-id <id>
tasks runs enqueue --module <module> --task <task> --payload-json <json>|--payload-file <path>
tasks runs control --run-id <id> --command tasks.pause|tasks.resume|tasks.cancel|tasks.drain --yes
tasks runs cancel --run-id <id> --yes
tasks runs retry --run-id <id> --yes
```

## Admin API

`Gma.Modules.TaskRuntime.AdminApi` maps admin-only endpoints under:

```text
/api/admin/tasks/runs
```

The API uses the same application commands and queries as the CLI. It is intended for operator tooling, not public product workflows.

## Permissions

The module declares:

| Permission | Purpose |
| --- | --- |
| `tasks.runs.read` | List, inspect, and view task run stats. |
| `tasks.runs.create` | Enqueue task runs. |
| `tasks.runs.cancel` | Cancel task runs. |
| `tasks.runs.retry` | Retry terminal task runs. |
| `tasks.runs.control` | Send control messages to running task handlers. |

Task runtime permissions are global operator permissions and are not scope-aware by default.

## Persistence

Schema:

```text
tasks
```

Migration history table:

```text
tasks.__ef_migrations_history
```

Tables:

- `task_runs`
- `task_control_messages`
- `task_scope_states`
- `task_scope_destroy_operations`
- `task_scope_destroy_receipts`

Provider-specific migrations exist for SQL Server and PostgreSQL. Tests and deployment automation apply migrations explicitly; default hosts do not auto-migrate.

Terminal history cleanup is optional and disabled by default. Durable control-message expiry remains active even when deletion is disabled:

```json
{
  "TaskRuntimeRetention": {
    "Enabled": false,
    "SucceededRunRetention": "30.00:00:00",
    "FailedRunRetention": "90.00:00:00",
    "CanceledRunRetention": "30.00:00:00",
    "TimedOutRunRetention": "90.00:00:00",
    "HandledControlRetention": "30.00:00:00",
    "FailedControlRetention": "90.00:00:00",
    "ExpiredControlRetention": "30.00:00:00",
    "CleanupInterval": "01:00:00",
    "BatchSize": 500,
    "MaxBatchesPerStatusPerCycle": 10
  }
}
```

Only terminal runs and terminal control messages are eligible. Each status has an independent retention window, cleanup work is bounded per status and cycle, and a run is retained while any control-message history still references it.

## Scope Lifecycle

`ITaskRuntimeScopeLifecycle` is the product-neutral tenant cleanup facade. Its
first accepted destroy call closes enqueue, retry, control-message, and claim
admission for the exact `ScopeId`. It cancels queued work, requests cooperative
cancellation for leased work, and reports `Busy` until every run is terminal.
Control messages and runs are then removed in bounded stages before an
immutable payload-free receipt closes the scope.

Admission takes a shared transaction-scoped key lock and lifecycle mutation
takes its exclusive counterpart on PostgreSQL and SQL Server. Exact request
replay remains valid after closure;
reuse of an operation or scope with different coordinates returns a conflict.
Ordinary retention skips closing and closed scopes so it cannot race the final
proof sequence.

Task payloads, progress, errors, and actor fields are non-authoritative
operational copies. Products must export authoritative data through the module
that owns its meaning, decide whether a product tenant maps to `ScopeId`, and
invoke final cleanup outside the target scope. A scoped task cannot remove its
own run before its worker records completion.

## Production Operations

### Delivery and idempotency

Task execution is at least once. Runtime lease fencing prevents a stale worker from updating a reclaimed run, but it cannot undo domain or external side effects completed before a process stopped. Task-owning handlers must therefore make side effects semantically idempotent.

An enqueue deduplication key is active for `(module, task, scope, key)` while the canonical run is queued, leased, running, cancellation-requested, or retry-scheduled. Concurrent callers receive that canonical run. Terminal completion releases the identity; manually retrying a terminal run reacquires it and returns a conflict if another active run already owns the identity. Deduplication is a queue admission guarantee, not an exactly-once side-effect guarantee.

Keep payload-version handlers available while old queued runs can still execute. The runtime stores payload JSON, progress text, errors, requester identities, and control payloads. Do not put credentials or unnecessary personal data in them, keep payloads below the 256 KiB contract limit, and apply the product's database encryption, access, backup, and deletion policy.

### Capacity and leases

- Partition unrelated workloads into worker groups so a slow task class cannot consume every worker slot.
- Size `MaxConcurrency` from downstream capacity, not CPU alone, and keep `BatchSize` close to the immediately available concurrency on each worker host.
- Keep `HeartbeatInterval` comfortably below `LeaseDuration`; the default is one third. Allow enough lease time for expected database stalls and deployment jitter.
- Set `HandlerTimeout` to the task's real upper bound. Set `StaleHeartbeatTimeout` above normal heartbeat and lease jitter so the scanner does not classify healthy work as abandoned.
- Give each live worker process a unique `WorkerId`. Use a stable pod, machine, or deployment identity for `NodeId`. Lease generation still fences accidental identity reuse, but unique worker identities keep diagnostics trustworthy.
- Monitor queue depth and oldest-ready age by worker group, active runs, failure and timeout rates, claim latency, handler duration, and retention cleanup failures. Increase concurrency only after the downstream database and services have headroom.

On graceful shutdown the worker stops claiming, cancels in-process handlers, waits for them to exit, and leaves unfinished leases to expire. Give the host enough termination grace for cooperative cancellation and final database writes. Handlers must honor cancellation and tolerate replay. The `tasks.drain` control command is cooperative per run; cluster-wide deployment draining remains a host or orchestrator responsibility.

### Migrations, retention, and reads

Before applying the concurrency migration to an existing database, check active non-null deduplication keys grouped by module, task, scope, and key. Resolve duplicates first: the unique active-identity index intentionally makes unsafe pre-existing duplicates fail deployment instead of silently choosing a winner. Apply migrations before deploying workers that write the new lease and concurrency fields.

Retention stays disabled until the product has approved data-lifecycle values. Enable it only after backup, audit, investigation, and legal needs are understood; tune bounded batches so cleanup does not compete with claims. The bounded lifecycle pass still transitions due pending, delivered, or failed controls to durable `Expired` state on `CleanupInterval`, even when deletion is disabled; worker reads also expire due messages immediately. Terminal expired controls follow `ExpiredControlRetention` once cleanup is enabled.

Admin list reads return total count and deterministic bounded pages. Count and page queries are separate, so operators should treat totals as an eventually consistent snapshot while workers are changing the queue.

## Boundaries

- Task payload contracts belong to the module that owns the task.
- Task handlers are registered explicitly through shared task registration helpers.
- `TaskRuntime` persists run state and exposes operator controls; it does not know module domain internals.
- Default API/admin hosts do not start workers or register the task admin front doors.
- External schedulers remain optional adapters that enqueue the same `TaskRunRequest` shape.

See [Tasks and Daemons](https://github.com/SadPossum/GMA-Framework/blob/dev/docs/architecture/tasks-and-daemons.md) for the shared task model. Skeleton repositories can include TaskSamples-style example handlers to show task payload and handler composition end to end.
