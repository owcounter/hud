# Local replacement bundle

Local testing only. No Docker Hub, Cloudflare Pages or GitHub release changes.
PowerShell 7 is required for these scripts. The win-x64 applications include .NET;
neither source checkout is needed to run a completed bundle. Windows native OCR
dependencies are smoke-tested on the development machine, not a clean Windows VM.

The HUD itself builds standalone. `Vision/` holds synced copies of the OWMETA
frame preparation and worker protocol (`tools/portrait-prototype/sync-hud-vision.ps1`
in the OWMETA repository); edit them there, never here. The OWMETA source is only
needed to build the private recognition worker for a bundle; publishing refuses
copies that differ from that worker source.

Build from the HUD checkout with explicit matching source and an existing frozen
model pack (never copy the whole checkout or an old `bin` directory):

```powershell
pwsh -File tools/Publish-LocalBundle.ps1 -OwmetaSourceRoot C:/path/to/owmeta -PackPath C:/path/to/model-pack -OutputPath C:/path/to/new-output
```

The output directory must not exist. Publishing uses an isolated `.build`
directory, preserves inputs and existing playtest binaries, and creates `bundle/`
plus `owmeta-local-win-x64.zip`. Only the six checksummed model assets and their
manifest are copied. Model thresholds, encoder and gallery are unchanged. Build
and model IDs and SHA-256 checksums identify the exact packaged versions. Checksums
detect accidental mixing/corruption, not authenticity of an untrusted download.

After extracting the zip:

```powershell
pwsh -File bundle/Start-LocalBundle.ps1 -VerifyOnly
pwsh -File bundle/Start-LocalBundle.ps1 -ApiUrl http://127.0.0.1:8001 -CapturePath C:/path/to/local-captures
```

A matching local backend must already be available; this launcher does not start
the API/database or deploy anything. Log in normally. Never place OAuth tokens in
source, share them in command lines or include them in an archive. The new HUD
rejects an old backend response without server-owned history. UNKNOWN retains
previous known heroes (including the main player); HIDDEN and known-map resets
are respected. Same-map match boundaries remain unsupported.

The launcher checks bundle hashes and worker build/model identity, refuses an
existing HUD or incompatible worker, and records captures outside the bundle. It
waits for HUD exit and stops only a worker it started. A reused worker is left
running. `-WorkerPort 17866` isolates a test from the normal port; both child
processes inherit it, and the worker remains bound to loopback only.

The GitHub release workflow is intentionally not wired to unpublished local
sources/model assets yet. Before public release, choose a versioned shared-source
dependency and approved model artifact, wire this tested publishing path into the
release workflow, verify a clean Windows machine and complete license review.
Blizzard artwork redistribution and ImageSharp eligibility remain unverified;
the model manifest preserves the dependency/license caveats. No universal accuracy
claim follows from packaging tests. Do not distribute local bundles publicly.
