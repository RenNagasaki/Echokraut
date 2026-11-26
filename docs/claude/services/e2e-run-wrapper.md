## E2E: wrapper mixed state (B8) and wrapper CLI cases (B10)

Moved verbatim from `e2e-run.md` (2026-11-26, size budget).

**Mixed wrapper state (B8)**, `Stages/MixedWrapperStage.cs` (stage `wrapper-mixed`, right after `first-time`, only with the request's
`wrapperVersion` — `start.sh --wrapper-version <tag>`; excludes `testWrapper`): `first-time` resolves the tag before touching anything
(`IEchokrauTtsInstanceService.SelectWrapperReleaseAsync` → GitHub `releases/tags/<tag>` (`GitHubReleaseParser.ReleaseByTagUrl`) → `_foundRelease`,
so the normal `Install` fetches and records that release; the update state stays `NotChecked`) and fails when another tag got recorded. Parts, each
blocked by a failed one before it: `:installed` (recorded tag + the installer's `Mode: echokrautts` `url=` names `/<tag>/`), `:greyed` (engines whose
catalog `minVersion` is above the tag — `E2eMixedWrapperChecks.Blocked`, own `Version` compare, not `WrapperUpdatePolicy` — are listed unavailable
with the localized "(from wrapper version X)" suffix, the rendered dropdown equals `ForConfig`, a pick of each is refused: engine and process
unchanged; skipped when nothing is blocked), `:update-offered` (first click on the wrapper button → `UpdateAvailable`, latest ≠ tag, tab shows
"Update" + `BuildVersionLabel`), `:update` (second click → stop + update, frame op `update`, ready, recorded = latest, `Mode: updateechokrautts`
url names it, same voice count, button back to "Check for updates"), `:unlocked` (entries plain, rendered dropdown again, pick of the first blocked
engine the request lists → `ttsBackend=<id>` Mode line + ready, then the original engine picked back). The clicks go through
`OwnedWindowActions` (`action:wrapper-update`, `action:engine-pick:<id>`): `E2eSweepSteps.Action` makes them runnable by name but keeps them out
of `Names`, so the B5 sweep never presses them. Backend-tab navigation (`E2eBackendTab`) is shared with `ui`. Nothing names a release, so the
stage serves every future mixed state. ⚠ The catalog comes from the tag `RemoteUrls.json` names — on a dev build GitHub `main` (see B3), so the
run needs the DLL built against the embedded `RemoteUrls.json` while `main` still names the old release.
**Wrapper CLI cases (B10)**, no game: `tools/e2e/wrapper-cli.sh [--fresh] [qwen-cpu|pocket-ja|pocket6-fr …]` copies the `../echokrautts`
working tree to its own PC folder (default `E:\EkE2E-wrapper-cli`, `EKE2E_WRAPPER_ROOT`; never the E2E install root), runs
`wrapper-cli.ps1` per case (`bootstrap\install_win.ps1 --start` with free bootstrap args, port 8799, uv cache + uv Python inside the folder
so C: stays clean) and judges with `wrapper_cli_judge.py` against the wrapper's `src/i18n.py` templates. Reports land in
`.claude/e2e-reports/wrapper-cli-<ts>/`. Not `windows-test.sh`: it takes no `--language`/free args and its `/tts` is fixed to `de`.
⚠ PowerShell `Get-Content` shows the live NDJSON lines garbled over ssh (ANSI read); the fetched files are UTF-8 and judged as such.
