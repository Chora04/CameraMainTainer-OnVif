# Resource limits and large fleets

Updated: 2026-10-13. The objective is bounded resource use, not a guarantee of
20,000 completed camera checks per hour.

## Defaults

- Check concurrency is detected at startup: **2 check slots per available physical core**.
  Windows topology is read through `GetLogicalProcessorInformationEx`, not inferred
  by dividing the logical CPU count by two. Single-group process affinity and the
  runtime CPU allowance are respected. All active physical cores are counted,
  including non-SMT cores. Old `Health:Concurrency` overrides are ignored.
  Examples: 4 cores -> 8 slots; 8 cores -> 16 slots, with or without SMT.
  These are shared async check slots, not CPU pinning or hardware Hyper-Threading.
  OS scheduling decides where continuations execute. Restart after changing CPU limits.
  Unsupported OS/topology failure uses 2 slots and logs a warning, rather than guessing.
  See [Windows CPU topology](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex).
- `Health:CycleIntervalSeconds = 60`: the existing pause between completed queue passes.
- The configured queue order, per-camera minimum intervals, and stable-camera policy are unchanged.
- `Snapshots:CacheMegabytes = 128`: maximum retained JPEG payload in the preview cache,
  not a limit on the entire process. At most 1,024 frames are retained; least recently
  used frames are evicted first. Last-frame fallback expires after 30 minutes.
- `Snapshots:ConcurrentDownloads = 4`: simultaneous preview fetches, separate from
  automatic health checks. At most 64 preview requests can wait or run. A request
  has a 25-second deadline, including waiting and reading the response body.
- Viewers of the same camera reuse a successful frame for 2 seconds. Configuration
  and credential changes invalidate the cached frame/URI. Stale fallback responses
  retain `X-Frame-Stale` and `X-Frame-Captured-At` headers.

## Changes

- Camera registry: SQL pagination (50/100/200 rows), filter counts before pagination,
  and one batched request for visible changed IDs. No first-1,000-camera UI cutoff.
- Dashboard: select eight cameras and five authorized cases in SQL; count cases
  separately. Health summaries contain counters, not all active cases or their details.
- Read-only camera lists no longer load credentials or track the fleet in EF Core.
- Camera registry: automatic-check inclusion is updated per camera, and excluded
  devices have a dedicated filtered view without removing them from the registry.
- Explicit batch checks create at most twice the available physical-core count in service scopes,
  instead of a scope and waiting task for every requested camera.
- Firmware worker: ascending-ID batches of 100, fresh scope per batch, no 10,000-camera cutoff.
- JPEG analysis uses codec-level reduced-resolution decoding before grayscale resizing.
  Original preview JPEGs remain unchanged. Source/decoded pixel and compressed-byte
  limits prevent excessive image allocations. Reduced decoding can slightly change
  pixel statistics; it is not a bit-for-bit identical resampling algorithm.
  See [SKCodec scaling](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skcodec.getscaleddimensions).
- SQL command logging defaults to Warning; application logging defaults to Information.
- Health updates are deduplicated into 250 ms SignalR batches. The 60-second fallback,
  case visibility rules, and unsaved-form protection remain active.

## Verification and limits

- C# tests include 20,000 synthetic camera records in an isolated in-memory SQLite database:
  last-page access, counts, queue order, partial reads and untracked credential-free lists.
- Cache tests cover 20,000 insertions, eviction, expiry, credential changes, concurrent
  readers, download limits, cancellation, timeouts, overload and stale fallback.
- JavaScript tests cover visible-ID batching, camera membership changes, responsive
  layouts, alert workflows, realtime refresh and notification interactions.
- These tests do not measure 20,000 real ONVIF connections, JPEG downloads, sustained
  disk writes, many simultaneous users, or production CPU/RAM percentages.
- SQLite, case/history size, camera response times and network capacity remain real
  capacity constraints. Measure a representative deployment before choosing hardware.
- Queue metadata responses remain proportional to fleet size; normal registry reads
  remain paged and credential-free.

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```
