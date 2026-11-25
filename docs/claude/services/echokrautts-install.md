## EchokrauTTS engine — install, update, custom data, voice pack per language, engine catalog

- **Local install** (`Helper/Functional/`): `LocalInstallerProvisioner` (DRY provisioning + BLK-5 version handshake against `Configuration.InstalledInstallerVersion` / remote `InstallerVersion`) — used by BOTH `AlltalkInstanceService` and `EchokrauTtsInstanceService`. `TtsPaths` (per-engine subfolders under `TtsInstallRoot`), `TtsInstallDetection` (is-installed probes), `DirectoryMerge` (voice copy on engine switch).
- **Standalone installer** `EchokrautLocalInstaller/` (separate project, released as `ELI-*` GitHub tags — never influences plugin versioning): mode `echokrautts <root> <url> <isWin> <port> <lang> <parentPid> [ttsBackend] [xttsFp16]` downloads-if-url → runs bootstrap → parses NDJSON stdout → writes `Ready.EchokrauTTS.txt` on `ready`.
- **Wrapper update handshake (user-triggered, data-preserving).** `EchokrauTtsData.InstalledWrapperVersion` is what's on disk. The newest release is **looked up on demand** via `RemoteUrlsData.EchokrauTtsReleasesUrl` (GitHub `releases/latest`) when the user presses the button — **never on a timer or at startup**: unauthenticated GitHub API calls are capped at **60/h per user IP**, and the plugin already fetches several files from GitHub at launch (`JsonDataService` has retry+backoff precisely because those bursts hit 429). `RemoteUrlsData.EchokrauTtsVersion`/`EchokrauTtsUrl` remain the shipped baseline for fresh installs (`RemoteUrlsJsonTests` asserts the tag appears inside the URL).
  - `GitHubReleaseParser` (pure, tested) pulls **tag AND zip asset URL out of the same release** — taking only the tag and keeping the baseline URL would install the old zip under the new version's name. `EchokrauTtsInstanceService._foundRelease` then overrides both (`LatestWrapperVersion`, `WrapperDownloadUrl`), and `MarkInstalled`/`MarkUpdated` record `LatestWrapperVersion`, i.e. the tag of the zip actually downloaded.
  - Asset choice: first `.zip` whose name mentions EchokrauTTS, else the first `.zip`. A release with no zip is a **failure**, not "no update" — it cannot be installed, and "up to date" would hide it.
  - `WrapperUpdateState` (`Enums/`) drives one button with two jobs: `NotChecked`/`CheckFailed` → "Check for updates" (clickable), `Checking` → disabled, `UpdateAvailable` → "Update" (clickable), `UpToDate` → "Update" disabled. A failed check deliberately returns to "Check for updates" — showing a disabled "Update" would read as "you are current", which a failed lookup does not establish. HTTP 403 is reported with a rate-limit hint.
  - **The state must be reset after an install/update, not only after a check.** `MarkInstalled`/`MarkUpdated` write the new `InstalledWrapperVersion`; without `RefreshUpdateStateAfterInstall()` (→ `WrapperUpdatePolicy.StateAfterInstall`) the state stayed `UpdateAvailable` and the button kept offering an update that had already run. It goes back to `NotChecked` ("Check for updates") rather than `UpToDate`, because `UpToDate` is a dead state and **dimming a KamiToolKit node does not block its clicks** (`Echokraut/Windows/CLAUDE.md` → "Greyed out = unusable") — the first attempt at this fix greyed the button out and it stayed clickable. `_foundRelease` is kept so a later reinstall still pulls that release's zip.
  - `RemoteUrlsReachabilityTests` calls the real API and asserts the parser accepts the live release; 403/429 (rate limit) is tolerated because it says nothing about the URL being correct.  - `Helper/Functional/WrapperUpdatePolicy` is the pure comparison (`IsUpdateAvailable` + label building; ordinal string compare, never version parsing — a rollback published upstream is also "update available"). Empty remote tag ⇒ never offered. An empty *installed* tag on a local install never reaches the policy: `Configuration.MigrateWrapperVersionForExistingInstalls` backfills it once with `WrapperUpdatePolicy.AssumedLegacyVersion` (pre-handshake installs can only run the one published release, so "?" + a pointless re-download would be wrong). Should it reach the policy anyway (Remote/None, or a hand-edited config), it counts as a mismatch.
  - `IEchokrauTtsInstanceService.UpdateWrapper()` reuses `Launch`/`RunInstance`: same download-then-serve shape as `Install()`, but installer mode `updateechokrautts` instead of `echokrautts`, **`DownloadVoicePack` is skipped** (it wipes `samples/` first and would take the user's voices with it) and `MarkUpdated()` records the tag without touching `LocalInstall`/`FirstTime`.
  - **`Launch`/`RunInstance` take a `WrapperLaunchMode`** (`Enums/`), not the former `(bool download, bool update)` pair. The pair could express four combinations of which only three meant anything, and it could not say "install, but without downloading" at all — which is exactly what the test install needs. Three questions are derived from the mode: `NeedsDownload` (is the installer handed a URL), `IsInstallPhase` (progress bar + post-ready bookkeeping) and the installer's argv mode.
- **Test install of an untagged wrapper (developer-only, `WrapperLaunchMode.TestInstall`).**
  `Helper/Functional/TestWrapperInstall` gates a second install button on a **dev load** (`IsDev`) plus
  a locally built zip in the wrapper checkout next to the plugin checkout: `ZipPathFor(dll, isDev)` =
  the DLL folder five levels up (`Debug`→`x64`→`bin`→project→repo) + `Echokrautts/wrapper/EchokrauTTS.zip`,
  computed in `ServiceBuilder` and handed to `EchokrauTtsInstanceService` (`TestWrapperZipPath`, null
  outside a dev load). Until 2026-11-22 this was an absolute path of the developer's machine as a
  constant — stale after the checkouts moved, and a machine path must not be in the public repo
  (privacy gate). `tools/e2e/place-test-wrapper.ps1` derives the same target from
  `DevPluginLoadLocations`; keep both rules in sync.
  - **Own engine catalog:** a test build is at no tag, so the tag's `engines.json` would hide the
    engines it adds. `tools/e2e/push-test-wrapper.sh` (build from the `echokrautts` working tree via
    `build-release-zip.py` + the checkout's `engines.json` appended at the zip root, then
    `place-test-wrapper.ps1`) ships it; after a test install (and on every start while
    `InstalledWrapperVersion` is the `VersionMarker`) the instance service calls
    `ITtsEngineCatalogService.UseLocalCatalog(<installRoot>/echokrautts/engines.json)`, which **pins**
    the list: a remote fetch still in flight no longer overwrites it. A zip built by hand without
    `engines.json` keeps the normal remote catalog. The marker is unparseable, so
    `WrapperUpdatePolicy.IsAtLeast` treats every `minVersion` as met.
  - **E2E:** request `"testWrapper": true` (`start.sh --test-wrapper`) → `first-time` calls
    `IFirstTimeWizardController.StartInstall(testWrapper: true)` (→ `InstallTestWrapper`) and fails
    before touching the root when the zip is missing.
  `EchokrauTtsInstanceService.ExtractTestZip` unpacks it
  over `echokrautts/` (wholesale, `overwriteFiles: true`, no preserve list — the same unpack a real
  Install does), the installer is then handed an **empty URL** so it skips its download and bootstraps
  what is on disk, and the post-ready phase runs the Install branch (voice pack + `MarkInstalled`).
  - **Why it exists:** before this, the install path could not be exercised until release day. The
    download URL comes from `RemoteUrls.json` fetched from the plugin's `main` branch, and the remote
    value **beats** the embedded copy (`MergeWithFallback`/`StringOrFallback` only fall back on
    *empty* fields) — so editing the local file changes nothing at runtime. And the update check asks
    GitHub for `/releases/latest`, which **ignores drafts and pre-releases**, so a pre-release is not
    a workaround either. Copying the wrapper into place and pressing Start tests the *wrapper*, but
    never the *install*.
  - `MarkInstalled` takes the tag to record; the test install passes `TestWrapperInstall.VersionMarker`
    (`"test (local zip)"`) instead of `LatestWrapperVersion`. Because `WrapperUpdatePolicy` compares
    tags as opaque strings, that marker keeps the Backend tab honest **and** leaves every real release
    reading as "update available", so a test build is always replaceable.
  - **Hard-coded on purpose, no config value:** the button unpacks and executes an unreviewed archive,
    so the path must not be steerable — a user-settable path would turn a developer convenience into
    a way to talk someone into running a foreign zip. Being an absolute path inside a source checkout
    is what makes it invisible on an installed copy.
  - **No player identity in the gate.** An earlier draft also required a specific character name. It
    was removed: an identifying value committed to a public repo ties the author to a game account,
    and it bought nothing — the path already restricts this to one machine, and a freely chosen name
    was never a boundary. Consequence: `TestInstallAvailable` is now a bare disk probe, asked every
    frame by the UI, so `EchokrauTtsInstanceService` caches it behind a 5 s re-probe interval (a
    failing lookup per frame for every user otherwise; without the re-probe the developer would have
    to restart the game after building a zip).
  - The button is built into `NativeEchokrauTtsBuilder.BuildLocalInstance`'s install row, so it appears
    in **both** the Backend tab and the first-time wizard without either window knowing about it.
    `Launch` re-checks `TestInstallAvailable` because dimming a KamiToolKit node does not block clicks.
  - Installer side: `updateechokrautts` takes the SAME args as `echokrautts` and only changes unpacking — `ExtractPreservingUserData` walks the zip entry-by-entry and skips anything whose **first** path segment is in `Constants.PRESERVEDECHOKRAUTTSFOLDERS` (`samples`, `models`). First segment only, so a python package named `models` deeper in the tree still updates. Zip-slip guarded, nothing is ever deleted (a file dropped by the new release stays behind — the safe direction for a mode that promises data survival).
  - `LatestWrapperVersion` is exposed on `IEchokrauTtsInstanceService` rather than injecting `IRemoteUrlService` into the windows — that service already owns the wrapper lifecycle and holds the URL config.
  - ⚠ **Needs the installer re-release**: an installer that predates the mode falls through the arg switch and exits immediately, which the plugin reports as "Installer exited before EchokrauTTS became ready". Order on release: publish the `ELI-*` release, then bump `installerUrl` + `installerVersion` in `RemoteUrls.json` (`RemoteUrlsReachabilityTests` fails on an unpublished tag). The asset name is free — `LocalInstallerProvisioner` takes the file name from the URL (since `ELI-1.3.1.0`: `EchokrautLocalInstaller-<ver>.zip`). Current: `ELI-1.3.1.0` + wrapper `0.0.1.9`.
- **Custom data (own model + samples)** — analog to AllTalk's "Install only custom data". `EchokrauTtsData.CustomModelUrl`/`CustomVoicesUrl` (direct or Google Drive zip URLs) → `IEchokrauTtsInstanceService.InstallCustomData(eventId, installProcess:false)` runs the installer's **`installcustomdataek`** mode off-thread: model zip → `echokrautts/models/echokraut_custom` (replaces prior custom model; installer flattens a single wrapping folder), voice samples zip → `echokrautts/samples` (**additive** — existing voices preserved). Then restarts the wrapper (when running or auto-start on) so the model reloads. **Voice-pack skip:** when `CustomVoicesUrl` is set, the fresh-install voice pack download is skipped in BOTH `AlltalkInstanceService.Install` and `EchokrauTtsInstanceService.RunInstance` — the pack service wipes the voices/samples folder before unpacking, so running it would clobber the user's custom voices (AllTalk installs them during the install step; EchokrauTTS gets them via a separate custom-data install). The **wrapper auto-detects** `models/echokraut_custom` for the ACTIVE engine: F5 picks the first `*.safetensors`/`*.pt`/`*.ckpt` (+ optional `vocab.txt`/`arch.txt`) via `models.resolve_custom_model`; XTTS uses the dir when it holds `config.json` + a weight file via `xtts_backend._resolve_custom_model_dir`. No config passing — presence of the folder is the signal. UI = 2 URL inputs + button in the shared `NativeEchokrauTtsBuilder`. **Needs the installer re-release** (new mode lives in the installer binary).
- **Voice pack per client language.** `VoicePackService` no longer takes one fixed URL: it resolves
  `RemoteUrlsData.VoicePackUrls` (language key → URL, same keys as `VoiceNameUrls`) against the
  client language via `Helper/Functional/VoicePackUrlResolver` — the language's own pack, else
  English, else the legacy single `VoicePackUrl` so an older `RemoteUrls.json` keeps working.
  Publishing a pack for a new language is one line in `RemoteUrls.json`, no plugin release.
  - The language is injected as `Func<ClientLanguage>`, not captured once: a manual re-download from
    the Game Data Tools window may happen long after startup, and the user can switch client language
    in between.
  - The service logs which case applied. In a non-English client without its own pack the voices
    *will* sound English, and that is worth saying rather than leaving the user to guess.
  - `ClientLanguageKeys` (`Helper/Functional/`) holds the one spelling of those keys, shared with
    `JsonDataService` — a key spelled differently in one place does not fail loudly, it silently
    falls back to English.
- **Engine catalog (`ITtsEngineCatalogService` / `TtsEngineCatalogService`).** The sub-engine list is read from the
  wrapper repo's `engines.json`, not hard-coded in the plugin — a wrapper release that adds an engine shows up without a
  plugin release. URL template `RemoteUrlsData.EchokrauTtsEnginesUrl`, `{version}` → `EchokrauTtsVersion`; on failure the
  wrapper's default branch, then the embedded `Resources/Engines.json`. **The tag is tried first on purpose:** reading the
  branch would advertise engines no published wrapper ships yet; the branch exists only for the case where a release
  predates the file (`0.0.0.2` does).
  - **The fetch runs off the constructor** (`Task.Run`) and the embedded list is live immediately: the catalog is
    UI-only, and blocking startup — let alone a UI frame — on two HTTP round trips to fill a dropdown is not worth it. A
    window built in the seconds before the answer arrives shows the shipped list, one restart behind at worst.
  - Exposed to the UI as `IEchokrauTtsInstanceService.AvailableEngines` rather than injecting the catalog into both
    windows: that service already owns the installed wrapper version the list is judged against (same reasoning as
    `LatestWrapperVersion` above). `SwitchTtsBackend(string engineId)` persists the id and restarts a running instance.
  - A dropdown cannot grey out a single entry, so an engine the installed wrapper is too old for is refused **in the
    click handler** (`OnOptionSelected` puts the label back); the `"(from wrapper version X)"` suffix is only the
    explanation. Same rule as the Update button: the look never guards, the code does.
- **⚠ Deploy coupling:** Local EchokrauTTS only works once the user publishes the installer (`ELI-1.1.0.0`) + wrapper releases AND points `RemoteUrls.json` `installerUrl`/`installerVersion`/`echokrauTtsUrl` at them. Remote EchokrauTTS works today. `RemoteUrlsData` must copy `InstallerVersion` + `EchokrauTtsUrl` in `MergeWithFallback` (else BLK-5 is off).
