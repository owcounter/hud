# Local packaging validation — 2026-09-25

Docker Hub, Cloudflare Pages and GitHub release publishing are unchanged. No
commits, pushes, uploads or deployments were made. Existing running HUD and
workers, including the loopback worker on port 17865, were preserved.

- Explicit source path builds a self-contained win-x64 HUD and worker into a
  fresh output directory, including the frozen `playtest-pack-04` assets.
- Build without a source path fails immediately with an actionable message.
- Reusing an existing output directory is rejected without overwriting it.
- All 32 vision tests pass, including port validation; HUD tests pass, including
  server history, placement and toggle/capture policies. Existing CS0162 warning
  in ScreenshotMonitoringService remains; this change does not alter that logic.
- The first packaging attempt caught an overly broad credential filename filter
  matching the runtime's OAuth.dll. Fixed to target credential files, not libraries.
- Complete bundle and ZIP extracted to a different directory both pass checksums,
  production-URL rejection, corruption rejection and live worker smoke tests.
- Packaged worker tests on isolated port 17866 exercise actual ONNX/OCR startup,
  two consecutive supported prepared-frame requests and malformed-input HTTP 400.
  Test-owned workers are stopped afterward; no HUD instance is launched.
- CodeRabbit reviewed the eight packaging/protocol/worker source and test files
  with zero findings. This results document was written after the review snapshot.
- Initial packaged replay: identity 2.97/0.93 ms and full worker 99.97/57.65 ms.
  These are two smoke-test samples, not a benchmark or new accuracy measurement;
  decode/HUD preparation and backend/network time are not included.

Artifacts are local and ignored under the OWMETA prototype's
`prototype-artifacts/local-bundle-20260925-02` and
`prototype-artifacts/local-bundle-relocated-20260925`. The ZIP is approximately
161 MiB. Failed first-build output remains available for diagnosis; no originals
or screenshots were removed.

Remaining validation: fresh gameplay against the matching local backend, clean
Windows-machine native dependency checks and acceptance accuracy. The completed
bundle has no source-checkout dependency at runtime. Future public release still
requires a versioned source/model delivery choice, release workflow wiring and
license approval; these are deliberately not deployed during local iteration.
