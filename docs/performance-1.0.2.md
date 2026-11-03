# Browsing and preview regression checks

For the requested same-machine WPF / 1.0.1 / 1.0.2 comparison, see the
[bilingual timing report](performance-comparison-1.0.2.md), with raw samples and
separate first-access, warm-access and window-ready measurements.

The native smoke test writes `performance.json` when `GARBRO_BENCHMARKS=1`.
The Linux x64/ARM64 release jobs enforce the agreed directory, warm archive,
image-cache and dispatcher budgets. Reports are retained with the job artifacts.
These are synthetic local-file measurements, not promises for every game or disk.

On the same Linux x64 machine, the previous one-shot worker took median 970 ms
for the fixture directory, 967 ms for a tiny XFL, and 1015 ms for a 72-byte PNG.
With the reusable worker, protocol round trips measured 2.3 ms for XFL, 0.6 ms
for PNG and 0.8 ms for the directory. First format initialization remained about
1.1 seconds and is now prewarmed in the background.

A native X11 run with software rendering measured:

| Operation | Time |
| --- | ---: |
| 10,000-file directory, including view update | 100 ms |
| 100,000-entry synthetic archive, including view update | 1.7–2.4 s |
| Small archive, median warm end-to-end access | 12 ms |
| Decoded-image cache hit, median view update | 8 ms |
| UI dispatcher delay P95 during the scenario | 11 ms |

`workerStages` separates initialization, resource work, payload deserialization and
parent round-trip time. Round-trip time includes process startup when cold and
protocol serialization/transport; the residual is not a separate network metric.
Uncached automatic previews use a 120 ms decoder debounce; cache hits are presented immediately.

Video tests inspect the last presented headless framebuffer without manually
rendering the control. The native Windows/X11 test also reads actual drawable
pixels from only the video area, excluding the slider, time labels and buttons.
The local native run recorded 21 different video regions in 24 captures.
Pause, seeking, manual playback, audio-device absence and cache removal are
covered separately. The test source has changing colours and a time display.

Resource regressions include sparse 3 GiB HEX paging, empty files, preview/file-name
encoding separation, nested archive navigation, source changes, timeout/cancellation,
worker crashes and idle expiry. Existing one-shot jobs, writer readback, extraction
limits and path checks remain in the complete core/release suites.

Physical audio hardware, arbitrary game variants and GPU/desktop combinations
beyond the recorded CI platforms still require normal device testing.
