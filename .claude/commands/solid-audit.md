---
description: Project-wide DRY/KISS/SOLID audit via multi-agent fan-out (writes a report, no code changes)
---
Run a thorough, project-wide DRY/KISS/SOLID audit of the Echokraut plugin.

Use the **Workflow** tool with `scriptPath: ".claude/workflows/solid-audit.js"` and **no `args`**, so the workflow runs its own Discover phase against the current file set (fans out one review agent per module, adversarially verifies each module's findings, then synthesizes). This is an explicit opt-in to multi-agent orchestration.

When it finishes:
1. Write the returned `reportMarkdown` to `docs/reviews/dry-kiss-solid-audit-<YYYY-MM-DD>.md` (use today's date).
2. Give a short summary: `totalConfirmed`, counts by principle and severity, and the top 3 cross-cutting themes.

The workflow only reads code and writes a report — it never modifies source.
