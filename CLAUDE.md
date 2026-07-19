# Echokraut

FFXIV Dalamud plugin — NPC dialogue TTS via AllTalk_TTS.

## Build
`dotnet build` — run tests with `dotnet test Echokraut.Tests/Echokraut.Tests.csproj`.

## SonarQube
- Project key: `echotools_echokraut_9b9e9dc6-3913-4c86-9a4d-57f0a73cf354`
- Exclusions configured: `KamiToolKit/**`, `Echotools/**`, `EchokrautLocalInstaller/**`

## Hook Enforcement (some CLAUDE.md rules are now deterministic gates)
Several enforceable rules are backed by Claude Code hooks, not just prose. Plan/design: `docs/plans/claude-md-rules-to-hooks.md`.
- **Global** (`~/.claude/hooks/`, registered in `~/.claude/settings.json`): `git-guard.js` (deny push / **CLAUDE.md-commit in Brunata repos only** / AI-attribution, ask on `git commit`), `block-submodule-edits.js` (deny edits under any `.gitmodules` path), `no-stray-docs.js` (ask on new stray `.md`). Note: CLAUDE.md is now committable in personal repos (policy reversed 2026-07-19); only repos under `F:/Git-Repositories/Brunata/` keep it local-only.
- **Dalamud tier** (same dir, self-scoped via the `KamiToolKit` submodule marker): `dalamud-mark-dirty.js` (PostToolUse) + `dalamud-verify-on-stop.js` (Stop → build+test gate, only when `.cs/.csproj` changed), `dalamud-log-method-warn.js` (advisory).
- **Echokraut tier** (committed in-repo): `.claude/hooks/echokraut-commit-gate.js` (PreToolUse Bash → deny `git commit` unless a changelog file is staged, EN/DE in sync, and `<Version>` > latest GitHub tag; merge commits exempt). Registered in the **committed** `.claude/settings.json`; the repo's own `.gitignore` ignores `.claude/*` and re-includes `.claude/settings.json` + `.claude/hooks/` (the global `~/.gitignore_global` no longer ignores `.claude/` as of 2026-07-19 — the repo block is now self-sufficient).
- A `type: "agent"` Stop hook runs a conservative DRY/KISS/SOLID review globally (experimental; fires a subagent at every turn-end — remove that one entry in `~/.claude/settings.json` to disable).

## Project Structure

```
Echokraut/
  Plugin.cs              — Entry point, DI wiring, lifecycle
  Services/              — All business logic (interface + impl pairs) → see Services/CLAUDE.md
    ServiceBuilder.cs    — DI registration
    ServiceContainer.cs  — Lazy DI container
    DatabaseService.cs   — SQLite data access (EF Core), schema migrations v1–v13
    VoiceClipManagerService — Business logic for Voice Clip Manager (play/generate/delete)
    Queue/               — Voice message queue (ConcurrentQueue)
    LuabParser.cs        — Lua 5.1 bytecode parser for quest scripts
    LgbParser.cs         — LGB (Level Group Binary) territory file parser
    DialogHarvestService — Batch dialog harvester
  Windows/               — Native-only UI layer (KamiToolKit) → see Windows/CLAUDE.md
    Native/              — FFXIV-native ATK windows
    Native/NativeVoiceClipManagerWindow — Voice clip tree view
    Native/NativeVoiceClipDetailWindow  — Voice clip detail per NPC instance
  DataClasses/           — Config, models, enums → see DataClasses/CLAUDE.md
    Database/            — EF Core entities (CharacterEntity, VoiceEntity, etc.)
  Enums/                 — Shared enumerations
  Helper/Functional/     — Stateless pure utilities (no DI)
  Localization/          — Loc.cs with DE/FR/JP translations
  Resources/             — RemoteUrls.json, embedded assets
  Backends/              — ITTSBackend clients: AllTalk + EchokrauTTS HTTP clients
Echokraut.Tests/         — xUnit + Moq tests
EchokrautLocalInstaller/ — Standalone local installer for AllTalk + EchokrauTTS (separate project, ELI-* tags)
KamiToolKit/             — Git submodule (DO NOT EDIT)
Echotools/               — Git submodule (DO NOT EDIT) — shared logging + UI components
  Logging/               — ILogService, EchoEventId, TextSource
  UI/                    — Reusable native UI nodes (PaginationBar, etc.)
OtterGui/                — Git submodule (DO NOT EDIT) — ImGui utilities (unused, kept for submodule)
```

## Architecture Overview
- Entry point: `Plugin.cs` — resolves all services from `ServiceContainer`.
- All state and logic in `Services/` as interface + implementation pairs.
- `Helper/Functional/` for stateless pure-utility classes (no DI needed).
- Cross-component communication via events — no `SetXxx` post-construction wiring.
- Native-only UI via `NativeWindowManager` (KamiToolKit). ImGui UI was removed.
- `DialogState` (static) holds cross-cutting UI state.
- **SQLite database** (`echokraut.db`) stores NPC/player mappings, voices, phonetics, muted dialogues, and voice clips. Managed by `IDatabaseService`/`DatabaseService` (EF Core). Plain settings remain in Dalamud's JSON config.

## Detailed Documentation
- **Services & business logic**: `Echokraut/Services/CLAUDE.md`
- **UI layer (Native)**: `Echokraut/Windows/CLAUDE.md`
- **Data models & config**: `Echokraut/DataClasses/CLAUDE.md`
- **Dalamud plugin conventions**: `../CLAUDE.md` (parent directory)

## Git Remotes
- `origin` → `https://gitlab.echotools.cloud/echotools/echokraut.git` (primary)
- `github` → `https://github.com/RenNagasaki/Echokraut.git` (mirror, auto-pushed via GitLab CI on master)

## Rules
- New features → `Services/`, follow interface → implementation pattern
- Register in `ServiceBuilder.cs`, inject via constructor
- No doc files unless asked
- **Tests are mandatory**: When adding, changing, or removing public/internal functions, always add, update, or remove corresponding tests in `Echokraut.Tests/`. Every task must end with `dotnet build` AND `dotnet test` passing with 0 failures.
- Tests live in `Echokraut.Tests/` using xUnit + Moq. One test file per class under test.
- **Keep CLAUDE.md files up to date**: Update the relevant CLAUDE.md whenever you discover patterns, constraints, gotchas, or conventions. If a rule applies to all Dalamud plugins, add it to `../CLAUDE.md` instead.

## Changelog Maintenance (mandatory per commit)

Every commit must contribute to a changelog file under `Echokraut/Resources/Changelogs/`. The user-facing changelog popup (`NativeChangelogWindow` + `IChangelogService`) reads these embedded files and shows entries between `Configuration.LastSeenChangelogVersion` and the current `Plugin.PluginVersion` to every user after a plugin update — so a commit without a changelog touch silently disappears from the user's "what's new" view.

### Per-commit workflow

Before staging a commit, run these steps in order:

1. **Compare project version against the latest release tag — on GitHub.**
   - "Latest release tag" means the highest-versioned tag on the GitHub mirror, **excluding any tag that starts with `ELI-`** (those tags belong to the standalone `EchokrautLocalInstaller` project, not the plugin, and must never influence Echokraut versioning).
   - Authoritative lookup command:
     ```bash
     git ls-remote --tags github 2>/dev/null \
       | awk -F/ '{print $NF}' \
       | grep -v '\^{}$' \
       | grep -E '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$' \
       | sort -V \
       | tail -1
     ```
     The strict `X.Y.Z.W` regex excludes both `ELI-*` (EchokrautLocalInstaller) and any one-off marker tags (`TEST`, etc.). `\^{}` strips peeled-tag artifacts; `sort -V` does version-aware sort so `0.10.0.0` correctly sorts after `0.9.9.9`.
   - Why GitHub and not local / GitLab tags: GitHub Releases are what users actually install from. Local tags can be created without ever pushing; GitLab tags can be in flight or out of sync. The mirror pipeline pushes GitLab → GitHub, so GitHub is the authoritative "what's been released" record.
   - Project version: `<Version>` element in `Echokraut/Echokraut.csproj`.
2. **If `<Version>` ≤ latest tag**, bump it before committing.
   - Default bump is **patch** (last segment +1). Example: tag `0.19.0.0`, project `0.19.0.0` → bump to `0.19.0.1`.
   - Bump **minor** (`0.19.0.0` → `0.19.1.0`) when the commit ships a user-visible feature significant enough to call out in release notes.
   - Bump **major/minor manually** for breaking changes — never do that automatically.
   - The `<Version>` value drives `Plugin.PluginVersion`, which is what `IChangelogService` compares against. A version that's not strictly greater than the latest released tag means the changelog popup logic can't distinguish "you're on the released version" from "you've got newer commits from the build pipeline".
3. **Update both changelog files** for the post-bump version: `Echokraut/Resources/Changelogs/v{NEW_VERSION}_EN.txt` and `_DE.txt`. Two cases:
   - **First commit on a new version** (file doesn't exist yet): create both files. Use any existing `v{X}_EN/DE.txt` as a template — `MAJOR NEW FEATURES` / `IMPROVEMENTS` / `BUG FIXES`, ASCII section headers, blank lines between entries (the section splitter in `NativeChangelogWindow` relies on those for rendering). **Also add two `<EmbeddedResource Include="Resources\Changelogs\v{NEW_VERSION}_{LANG}.txt" />` lines** to `Echokraut/Echokraut.csproj` (mirror the pattern of the existing entries). Without those, MSBuild won't embed the new files and the runtime resource enumeration silently misses them.
   - **Subsequent commits on the same version**: append a new bullet to the appropriate section (`MAJOR NEW FEATURES` for `[NEW]` items, `IMPROVEMENTS` for refinements, `BUG FIXES` for fixes). Keep the bullet prose short — match the existing tone (concrete + user-visible, not implementation detail).
   - **Multi-section commits**: a single commit can legitimately touch multiple sections (e.g. a bug fix that ships a small UX tweak alongside it). Write a bullet in each affected section.
4. **Both languages must stay in sync.** When you add an entry to `_EN.txt`, add the corresponding German translation to `_DE.txt` in the same position. FR/JA fall back to EN at runtime (no FR/JA changelog files yet).
5. **Internal-only refactors / test churn / CI tweaks** that have zero user-visible effect can skip the changelog with a one-line note in the commit message (`(no changelog: internal refactor)`). Use this sparingly — when in doubt, write the entry. The `INTERNAL / DEVELOPER` section was deliberately removed from `v0.19.0.0` because it noised up the user-facing popup; do not reintroduce it.
6. **Merge commits are exempt.** A `--no-ff` merge from a feature branch into `main` doesn't introduce content beyond what's already in the feature-branch commits (which already updated the changelog when they were made). Don't bump or touch the changelog on the merge.

### Worked example

Latest tag is `0.19.0.0`. csproj `<Version>` is `0.19.0.0`. You're about to commit a None-mode UI fix.

- Step 2: bump csproj `<Version>` to `0.19.0.1`.
- Step 3: file `v0.19.0.1_EN.txt` doesn't exist → create it with a `BUG FIXES` section containing the new bullet. Same for `v0.19.0.1_DE.txt`.
- csproj entry added to `<EmbeddedResource>` block (mirror the pattern used for `v0.19.0.0_*`).
- Stage csproj + both new changelog files + the actual fix → commit.

Next commit on the same version:

- Step 1: latest tag is still `0.19.0.0`, project is `0.19.0.1` (> tag) → no bump needed.
- Step 3: append the new bullet to the existing `v0.19.0.1_*.txt` files.

When a release tag is created (e.g. `0.19.0.1`):

- The next commit triggers step 2 again (project equals tag) → bump to `0.19.0.2` → new pair of changelog files created → cycle continues.

### Why this is per-commit, not per-PR

Echokraut commits go directly to the GitLab `main` branch (only larger features use feature branches). There's no PR review chokepoint where a "changelog reminder" would be enforced. The rule lives here so anyone making a commit (Claude or human) checks before staging.
