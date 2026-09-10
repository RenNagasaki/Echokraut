#!/usr/bin/env node
// PreToolUse(Bash) — ECHOKRAUT project tier (committed, repo-local).
// Enforces the per-commit changelog workflow from Echokraut/CLAUDE.md:
//   * every non-merge commit must stage a changelog file
//   * EN/DE changelog files must be staged together
//   * <Version> in Echokraut.csproj must be strictly greater than the latest
//     released GitHub tag (X.Y.Z.W, ELI-* excluded)
// Merge commits (.git/MERGE_HEAD present) are exempt. Offline -> version check
// degrades to a warning (allow).

const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const ALLOW = "{}";
const DENY = (reason) =>
  JSON.stringify({
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      permissionDecision: "deny",
      permissionDecisionReason: reason,
    },
  });

function sh(cmd, cwd, timeout = 10000) {
  return execSync(cmd, { cwd, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"], timeout });
}

function cmpVersion(a, b) {
  const pa = a.split(".").map(Number);
  const pb = b.split(".").map(Number);
  for (let i = 0; i < 4; i++) {
    const d = (pa[i] || 0) - (pb[i] || 0);
    if (d !== 0) return d < 0 ? -1 : 1;
  }
  return 0;
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let cmd = "";
  let cwd = process.cwd();
  try {
    const data = JSON.parse(raw || "{}");
    cmd = (data.tool_input && data.tool_input.command) || "";
    if (data.cwd) cwd = data.cwd;
  } catch {
    console.log(ALLOW);
    return;
  }

  if (!/\bgit\s+commit\b/.test(cmd)) {
    console.log(ALLOW);
    return;
  }

  // Only guard the Echokraut repo.
  if (!fs.existsSync(path.join(cwd, "Echokraut", "Echokraut.csproj"))) {
    console.log(ALLOW);
    return;
  }

  // Merge commits are exempt.
  if (fs.existsSync(path.join(cwd, ".git", "MERGE_HEAD"))) {
    console.log(ALLOW);
    return;
  }

  // Documented escape hatch: internal-only commits may skip the changelog with a
  // "(no changelog: ...)" note in the message. Skips changelog + EN/DE checks only;
  // the version gate below still applies.
  const skipChangelog = /no changelog/i.test(cmd);

  let staged = [];
  try {
    staged = sh("git diff --cached --name-only", cwd)
      .split(/\r?\n/)
      .map((s) => s.trim().replace(/\\/g, "/"))
      .filter(Boolean);
  } catch {
    console.log(ALLOW); // can't inspect index -> don't block
    return;
  }

  const changelogs = staged.filter((p) => /Echokraut\/Resources\/Changelogs\//i.test(p));

  // 1) Changelog presence (unless internal-only "(no changelog: ...)" escape).
  if (!skipChangelog && changelogs.length === 0) {
    console.log(
      DENY(
        "Regel (Echokraut/CLAUDE.md): »Jeder Commit muss eine Changelog-Datei beitragen.« Keine Datei unter Echokraut/Resources/Changelogs/ gestaged. Changelog-Eintrag ergänzen (EN + DE) und mitstagen — oder bei rein internen Änderungen die Commit-Notiz »(no changelog: …)« verwenden."
      )
    );
    return;
  }

  // 2) EN/DE sync.
  for (const p of skipChangelog ? [] : changelogs) {
    if (/_EN\.txt$/i.test(p)) {
      const de = p.replace(/_EN\.txt$/i, "_DE.txt");
      if (!staged.includes(de)) {
        console.log(DENY(`Regel: »EN und DE müssen synchron bleiben.« '${path.basename(p)}' ist gestaged, '${path.basename(de)}' fehlt im Stage.`));
        return;
      }
    }
    if (/_DE\.txt$/i.test(p)) {
      const en = p.replace(/_DE\.txt$/i, "_EN.txt");
      if (!staged.includes(en)) {
        console.log(DENY(`Regel: »EN und DE müssen synchron bleiben.« '${path.basename(p)}' ist gestaged, '${path.basename(en)}' fehlt im Stage.`));
        return;
      }
    }
  }

  // 3) Version > latest GitHub release tag.
  try {
    const csproj = fs.readFileSync(path.join(cwd, "Echokraut", "Echokraut.csproj"), "utf8");
    const vm = csproj.match(/<Version>\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)\s*<\/Version>/i);
    if (vm) {
      const version = vm[1];
      let latest = null;
      try {
        const ls = sh("git ls-remote --tags github", cwd, 10000);
        const tags = ls
          .split(/\r?\n/)
          .map((l) => {
            const m = l.match(/refs\/tags\/(.+?)(\^\{\})?$/);
            return m ? m[1] : null;
          })
          .filter((t) => t && /^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$/.test(t));
        for (const t of tags) {
          if (latest === null || cmpVersion(t, latest) > 0) latest = t;
        }
      } catch {
        // offline / no github remote -> warn only.
        process.stderr.write("[echokraut-commit-gate] Konnte GitHub-Tags nicht lesen — Version-Check übersprungen.\n");
      }
      if (latest && cmpVersion(version, latest) <= 0) {
        console.log(
          DENY(
            `Regel: »<Version> muss > letztem GitHub-Release-Tag sein.« csproj <Version>=${version}, letzter Tag=${latest}. Vor dem Commit bumpen (Default: Patch, letztes Segment +1).`
          )
        );
        return;
      }
    }
  } catch {
    /* csproj unreadable -> skip version check */
  }

  console.log(ALLOW);
});
