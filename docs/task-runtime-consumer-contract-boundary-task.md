# TaskRuntime Consumer Contract Boundary Task

Status: in progress
Date: 2026-08-10

## Goal

Expose TaskRuntime's existing persisted run operations through stable,
product-neutral Contracts so reusable-module consumers do not reference
TaskRuntime Application commands, queries, or errors. Preserve the current
transaction, validation, persistence, retry, cancellation, and control-message
behavior.

## Audit Finding

TaskRuntime owns enqueue, get, list, stats, cancel, retry, and control-message
use cases, but currently exposes them only as public CQRS messages in
`Gma.Modules.TaskRuntime.Application`. This makes consumers depend on an
implementation assembly and on `IRequestDispatcher`:

- BunkFy Ingestion AdminApi and AdminCli enqueue, read, cancel, and retry task
  runs through TaskRuntime Application;
- BunkFy Retention AdminApi and AdminCli read and retry task runs through
  TaskRuntime Application; and
- the GMA Skeleton integration fixture sends a control message through a
  TaskRuntime Application command.

TaskRuntime's own AdminApi and AdminCli also invoke those messages directly.
That is legal inside the module, but migrating the owned front doors to the
same Contracts facade lets the CQRS messages become internal and proves that
the public boundary covers the complete operational surface.

The generic run records, filters, state transitions, worker protocol, and store
ports already belong to `Gma.Framework.Tasks`. They do not need redesign or a
second copy in this module.

## Contract Decision

1. TaskRuntime Contracts owns three least-capability in-process facades:
   `ITaskRunEnqueuer`, `ITaskRunReader`, and `ITaskRunController`.
2. Contracts owns small request records for enqueue, list, stats, and control
   operations. Results continue to use `Gma.Framework.Results.Result` and the
   existing product-neutral DTOs from `Gma.Framework.Tasks`.
3. TaskRuntime Application provides one scoped implementation of the three
   facades. It delegates once through `IRequestDispatcher` to the existing
   handlers, so validation, transaction middleware, error mapping, and store
   behavior remain authoritative in one place.
4. Stable runtime operation errors move to Contracts as
   `TaskRuntimeOperationErrors`. Transport-only status parsing and CLI payload
   source/file errors move to Admin.Contracts as
   `TaskRuntimeAdminInputErrors`.
5. TaskRuntime AdminApi and AdminCli consume the Contracts facades. Once every
   known consumer is migrated, CQRS command and query records become internal
   Application details.
6. BunkFy Ingestion and Retention front doors reference TaskRuntime Contracts
   only. A BunkFy architecture test rejects future product-module references
   to reusable GMA module Application projects or namespaces.
7. The GMA Skeleton integration fixture sends control through Contracts. Host
   composition may still reference TaskRuntime Application to call
   `AddTaskRuntimeApplication`; composition is not a use-case dependency.
8. Framework remains unchanged. Persisted run operations belong to the
   optional TaskRuntime module, while generic task execution and storage
   primitives remain in `Gma.Framework.Tasks`.

## Security And Ownership

- The facades are in-process contracts; this slice adds no HTTP, CLI, remote,
  or messaging surface and grants no permissions.
- TaskRuntime validates runtime invariants, but it cannot infer a product's
  authorization or whether a run belongs to a product aggregate. Product
  callers must authorize first and validate module, task, and scope ownership
  before canceling or retrying a run.
- TaskRuntime's owned admin surfaces retain their existing global operator
  permissions and confirmation requirements.
- Payload JSON, actor identifiers, progress, and errors remain operational
  copies subject to the product's minimization, access, retention, and tenant
  lifecycle policies.
- The facade must not expose stores, persistence entities, DbContexts, raw
  handlers, or transaction controls.

## Efficiency And Compatibility

- Each facade call adds only an in-process dispatch and no additional database
  query, serialization pass, transaction, event, or projection.
- The implementation is scoped and shared across its three interfaces; it does
  not retain mutable state or duplicate runtime data.
- Existing command/query handlers remain the single behavior path. This avoids
  semantic drift between module-owned admin tools and product consumers.
- Making the CQRS messages internal is intentionally source-breaking for
  unsupported Application consumers. All known source-first consumers are
  migrated in the same slice; the stable replacement is the Contracts API.

## Delivery

- [ ] Add split Contracts facades, request records, operation errors, and
  contract/registration tests.
- [ ] Implement the Application facade over the current command/query handlers
  and make CQRS messages internal.
- [ ] Move TaskRuntime AdminApi/AdminCli to the Contracts facade and keep admin
  parsing errors in Admin.Contracts.
- [ ] Move the canonical GMA Skeleton integration consumer to Contracts and add
  a focused boundary guard where useful.
- [ ] Move BunkFy Ingestion and Retention consumers to Contracts and add the
  reusable-module boundary guard.
- [ ] Update TaskRuntime, Skeleton, and BunkFy documentation/pins after focused
  verification.
- [ ] Run one consolidated non-Docker gate per changed repository at the
  completed slice boundary, publish exact pins, and verify exact CI.

## Verification Plan

- While editing, run focused TaskRuntime facade, registration, handler, admin
  surface, Skeleton integration, BunkFy Ingestion/Retention, architecture, and
  composition tests.
- Run no local Docker/provider gate because this slice does not change a
  persistence model, migration, generated SQL shape, provider mapping,
  transaction boundary, or query behavior.
- At the coherent slice boundary, run the complete non-Docker TaskRuntime,
  canonical Skeleton, and BunkFy backend gates once, plus the lightweight
  BunkFy root composition/pointer gate before publication.
- Use the TaskRuntime exact-commit relational CI job as provider proof, then
  record exact source, pin, and CI evidence here.

## Verification Evidence

Pending implementation and slice-boundary verification.

## Not In This Slice

- changing task persistence, migrations, retention, leases, claim fencing,
  deduplication, or scope lifecycle behavior;
- adding product authorization, tenant-to-scope mapping, or task ownership
  policy to TaskRuntime;
- adding workflow/DAG orchestration, recurring schedules, or a remote
  TaskRuntime client protocol;
- moving TaskRuntime module operations into Framework; or
- redesigning product Ingestion or Retention admin workflows beyond replacing
  their TaskRuntime dependency boundary.
