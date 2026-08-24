# Process List Redesign — Storing, Passing, and Processing

Date: 2026-08-24
Status: Approved design, pending implementation plan

## Context and audit findings

The process list is the app's core data pipeline: `ProcessManager` enumerates OS processes on a timer and publishes them to the grid via `MainWindowViewModel`. The current design has defects at every stage.

**Storage**

- `Processes` exists twice — a settable `ObservableCollection<ProcessItem>` on both `ProcessManager` and `MainWindowViewModel`, kept "in sync" through a circular setter-push / PropertyChanged-pull dance (`MainWindowViewModel.cs`). `ProcessManager.Processes` is never actually replaced, so the sync machinery is dead weight.
- `ProcessCount` is a getter/setter hack whose setter only raises notifications; `CollectionChanged` additionally raises `OnPropertyChanged(nameof(Processes))` on every single add/remove.
- `CollectionChanged` is subscribed twice — once in the `ProcessManager` constructor and once in `MainWindowViewModel`'s constructor — so count/processes notifications fire in duplicate.
- The Domain service holds UI-thread-affine state (`ObservableCollection`, dispatcher) and derives from `ObservableObject`, coupling business logic to WPF.

**Passing**

- Export receives a lazy `IEnumerable<Process>` over the live collection, captured when the dialog opens but enumerated later while polling keeps mutating the source.
- Selected PIDs are captured by filtering the live collection, with no thread or timing guarantees documented.

**Processing**

- Stale rows: `Refresh()` only adds and removes; existing PIDs are never updated, so Path, Priority, and ThreadCount freeze at first-seen values for the lifetime of the session.
- Race: the deleted-process computation runs `Processes.Where(...)` on the timer thread while the UI thread mutates the same non-thread-safe collection.
- One `Dispatcher.Invoke` per added/removed item instead of one batched marshal per refresh.
- O(n²) diffing via nested `.Any()` linear scans over lists.
- No reentrancy guard: if enumeration exceeds the timer interval, refreshes stack up.
- Full re-enumeration every tick: a handle is opened to all ~300–500 processes and `MainModule` plus a WOW64 check run per process, even though nearly all processes are unchanged between ticks.

## Goals

1. Single source of truth for the process list; one binding path into the UI.
2. Live rows: every tick updates Path/Priority/ThreadCount of existing rows in place.
3. Per-tick cost proportional to *change*, not to system size: cheap snapshot diffing, expensive per-process work only for new PIDs.
4. Exactly one dispatcher marshal per refresh; no cross-thread access to the collection.
5. Materialized snapshots across every API boundary that outlives the call.
6. Selection survives refreshes naturally because surviving rows keep their instances.

Non-goals: sorting/filtering/virtualization improvements, changing the polling model (timer-driven stays), changing export formats.

## Architecture: storage

```
ProcessManager (Domain)
 ├─ ObservableCollection<ProcessItem> _items        (private, mutable)
 ├─ ReadOnlyObservableCollection<ProcessItem> Items (public, bound by UI)
 ├─ Dictionary<int, ProcessItem> _index             (PID → item, O(1) lookups)
 └─ Dictionary<int, EnrichedInfo> _enrichment       (PID → path+bitness, immutable per PID)
```

- The settable `Processes` properties on both `ProcessManager` and `MainWindowViewModel` are removed. The VM exposes `Items => _processManager.Items` as a getter-only pass-through; the XAML binding path is effectively unchanged.
- Removed outright: circular sync machinery, the `ProcessCount` setter hack, the duplicate `CollectionChanged` subscription, per-add `OnPropertyChanged(nameof(Processes))`. `ProcessCount` derives from `CollectionChanged` of the read-only wrapper.
- `Process` implements `INotifyPropertyChanged`. Grid columns bind `Process.*` directly, so in-place updates light up rows with zero XAML changes. `ProcessItem` remains purely the selection wrapper around `Process`.
- `_index` and `_items` are mutated only inside the dispatcher-apply step (single writer: UI thread).

## Refresh pipeline (every tick AND initial load — same code path)

1. **Guard:** polling skips a tick if one is in flight (`Interlocked` flag); user-initiated refresh waits instead of skipping (`SemaphoreSlim(1,1)`).
2. **Snapshot (off-thread):** an `ISystemProcessEnumerator` abstraction (implemented with NtApiDotNet) yields `(Pid, Name, ThreadCount, Ppid, BasePriority)` for all processes in roughly one syscall — no per-process handles opened yet.
3. **Diff (off-thread, O(n)):** against `_index.Keys`: `added` = snapshot − index; `removed` = index − snapshot; `changed` = intersection where snapshot fields differ from stored values. No nested linear scans.
4. **Enrich (off-thread, new PIDs only):** open the handle once → `Path` (via `MainModule`) + WOW64 bitness → cached forever in `_enrichment` (immutable per PID). Priority comes from the snapshot, so priority changes by external tools also show up.
5. **Apply (one `Dispatcher.Invoke`):**
   - removes → drop from `_items` and `_index`, and evict the PID from `_enrichment` (guards against PID reuse serving a stale path);
   - adds → create `ProcessItem`, insert into both;
   - updates → mutate existing `Process` fields, raising per-field `PropertyChanged`.

Inaccessible/protected processes stay visible with snapshot-level fields and an empty path — matching real Task Manager behavior. Today they silently vanish because enumeration skips them entirely. This is an intentional, approved behavior change.

Net effect per tick: ~1 syscall, handle opens only for genuinely new processes, one UI marshal — versus today's ~500 handle opens, O(n²) scans, up-to-N marshals, and permanently stale rows.

## Passing contracts

- **Export:** `ProcessManager.SnapshotForExport()` returns a materialized `IReadOnlyList<Process>` captured atomically on the UI thread. The lazy enumerable over the live collection is gone; the export dialog may hold the snapshot arbitrarily long with no mutation races.
- **Operations (terminate/priority):** unchanged shape — `IReadOnlyCollection<int>` PIDs in, `ProcessOpSummary` out. PIDs come from index lookups rather than filtering the live collection. After a priority change, the stored item's `Priority` updates through the same in-place mechanism refresh uses (one update path, not two).
- **Selection:** stays on `ProcessItem.IsSelected`. Selected PIDs are read synchronously on the UI thread inside command handlers — safe by construction, documented as UI-thread-only.

## Concurrency rules (explicit)

1. Collection and index are mutated only inside the dispatcher-apply step; single writer, UI thread.
2. Snapshot, diff, and enrich are pure functions over immutable inputs on worker threads; they never touch `_items`.
3. A `SemaphoreSlim(1,1)` serializes refreshes: polling uses `TryWait` → skip; manual uses `WaitAsync` → queue. Refreshes never overlap.
4. `async void` remains only at the timer callback boundary; everything below it is awaitable and exception-guarded.

## Error handling

- Per-process enrichment failure (access denied, exited mid-tick): logged at Debug; the process is shown with snapshot-level fields — degrade, don't drop, never abort the batch.
- Whole-pipeline failure: caught by the polling wrapper (existing `SafePollingRefreshAsync` pattern), logged as Warning; next tick retries. Manual refresh surfaces errors through the existing `IErrorHandler.GuardAsync`.
- Dispatcher apply: batch runs inside try/catch; `TaskCanceledException` during shutdown is swallowed (as today); anything else is logged.

## Testing

- New unit tests:
  - diff function (pure): added/removed/changed given two snapshots;
  - reentrancy guard: polling skip and manual queue behavior;
  - export snapshot immutability: mutating the store after capture leaves the exported list unchanged;
  - in-place update raises `PropertyChanged` per changed field.
- Reworked integration tests:
  - existing `ProcessManagerTests` keep working (constructor signature stable);
  - a fake-snapshot seam (`ISystemProcessEnumerator`) makes the refresh pipeline testable without real OS state;
  - end-to-end refresh test: snapshot changes produce correct add/update/remove batches through a stub dispatcher executing inline.
- No behavioral test coverage is removed.

## Migration notes

- `GetProcesses()` iterator and its per-process full enrichment disappear; replaced by the enumerator seam + enrichment cache.
- `LoadProcesses()` becomes the first pipeline run; polling reuses it unchanged.
- XAML needs no structural change; bindings resolve against the same property name exposed by the VM.
