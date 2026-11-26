export const meta = {
  name: 'solid-audit',
  description: 'Project-wide DRY/KISS/SOLID audit: per-module fan-out review, adversarial verify, synthesis',
  phases: [
    { title: 'Discover' },
    { title: 'Review' },
    { title: 'Verify' },
    { title: 'Synthesize' },
  ],
}

const FINDING_PROPS = {
  file: { type: 'string' },
  line: { type: ['integer', 'null'] },
  principle: { type: 'string', enum: ['DRY', 'KISS', 'SRP', 'OCP', 'LSP', 'ISP', 'DIP'] },
  severity: { type: 'string', enum: ['high', 'medium', 'low'] },
  title: { type: 'string' },
  detail: { type: 'string' },
  suggestion: { type: 'string' },
}

const DISCOVER_SCHEMA = {
  type: 'object', additionalProperties: false, required: ['units'],
  properties: {
    units: {
      type: 'array',
      items: {
        type: 'object', additionalProperties: false, required: ['name', 'files'],
        properties: { name: { type: 'string' }, files: { type: 'array', items: { type: 'string' } } },
      },
    },
  },
}

const REVIEW_SCHEMA = {
  type: 'object', additionalProperties: false, required: ['unit', 'findings'],
  properties: {
    unit: { type: 'string' },
    findings: {
      type: 'array',
      items: { type: 'object', additionalProperties: false, required: ['file', 'principle', 'severity', 'title', 'detail', 'suggestion'], properties: FINDING_PROPS },
    },
  },
}

const VERIFY_SCHEMA = {
  type: 'object', additionalProperties: false, required: ['unit', 'confirmed'],
  properties: {
    unit: { type: 'string' },
    confirmed: {
      type: 'array',
      items: { type: 'object', additionalProperties: false, required: ['file', 'principle', 'severity', 'title', 'detail', 'suggestion', 'verdictReason'], properties: { ...FINDING_PROPS, verdictReason: { type: 'string' } } },
    },
  },
}

const SYNTH_SCHEMA = {
  type: 'object', additionalProperties: false, required: ['reportMarkdown', 'totalConfirmed', 'byPrinciple', 'bySeverity'],
  properties: {
    reportMarkdown: { type: 'string' },
    totalConfirmed: { type: 'integer' },
    byPrinciple: { type: 'object', additionalProperties: { type: 'integer' } },
    bySeverity: { type: 'object', additionalProperties: { type: 'integer' } },
  },
}

const CONVENTIONS = 'Project: Echokraut, a C# FFXIV Dalamud plugin. INTENTIONAL conventions that must NOT be flagged as violations: DI via constructor injection; services are interface+implementation pairs, so interface files (IXxx.cs) are thin contracts by design — never flag them for "too few members" or SRP; generated files (e.g. *.Designer.cs) and the Loc.cs translation dictionary are off-limits; static stateless helpers under Helper/Functional are deliberately not DI; Native UI follows KamiToolKit patterns. Focus on IMPLEMENTATION files.'

function reviewPrompt(unit) {
  return `You are auditing EXISTING source code for DRY / KISS / SOLID violations.\n${CONVENTIONS}\n\nRead and review ONLY these files (unit "${unit.name}"):\n${unit.files.map((f) => '- ' + f).join('\n')}\n\nReport only concrete, defensible violations a senior C# reviewer would insist on fixing: real duplication that should be extracted (DRY); needless complexity / over-engineering / dead abstraction (KISS); a class or method carrying multiple unrelated responsibilities (SRP); other SOLID breaches (OCP/LSP/ISP/DIP) where they genuinely bite. Cite the file path and a line number. Do NOT report naming/style/formatting nitpicks, and do NOT flag the intentional conventions above. If the unit is clean, return an empty findings array. The "unit" field must equal "${unit.name}".`
}

function verifyPrompt(name, findings) {
  return `You are an adversarial verifier for unit "${name}". Candidate DRY/KISS/SOLID findings:\n\n${JSON.stringify(findings, null, 2)}\n\n${CONVENTIONS}\n\nFor EACH candidate, re-read the cited file and decide whether it is a REAL, significant, defensible violation — not a nitpick, not a false positive, not something explained by the intentional conventions. Default to REJECTING when uncertain. Keep only the confirmed ones; for each add a one-line verdictReason. Return confirmed (possibly empty). The "unit" field must equal "${name}".`
}

function synthPrompt(confirmed) {
  return `You are synthesizing a project-wide DRY/KISS/SOLID audit. Here are the VERIFIED findings across all modules (JSON):\n\n${JSON.stringify(confirmed, null, 2)}\n\nProduce a concise, actionable Markdown report:\n1. Executive summary with counts by principle and by severity.\n2. Cross-cutting THEMES first — especially DRY duplication patterns that span multiple files/modules — each with the affected files and a concrete refactor suggestion.\n3. The most impactful per-file findings (high/medium severity), grouped by module.\n4. An appendix listing every confirmed finding as "file:line — [principle/severity] title".\nKeep prose tight and concrete. Return reportMarkdown plus the counts (totalConfirmed, byPrinciple, bySeverity).`
}

// --- run ---
let units = Array.isArray(args) ? args : (args && args.units) || null

if (!units) {
  phase('Discover')
  const d = await agent(
    'Discover the review units for a DRY/KISS/SOLID audit of the Echokraut plugin. Using Bash/Glob from the repo root, list every C# source file under the Echokraut/ project directory, EXCLUDING obj/ and bin/, the git submodules (KamiToolKit/, Echotools/, OtterGui/), the Echokraut.Tests/ project, and EchokrautLocalInstaller/. Group files into units of at most 12 by their immediate subdirectory under Echokraut/ (Services, DataClasses, Windows, Helper, Enums, ...). Split oversized directories into numbered chunks (Services-1, Services-2, ...). Merge leftover units of <=2 files into a single "Misc" unit. Return the units array.',
    { label: 'discover', phase: 'Discover', schema: DISCOVER_SCHEMA, model: 'sonnet' }
  )
  units = d.units
}

log(`Auditing ${units.length} units (${units.reduce((n, u) => n + u.files.length, 0)} files)`)

const results = await pipeline(
  units,
  (unit) => agent(reviewPrompt(unit), { label: `review:${unit.name}`, phase: 'Review', schema: REVIEW_SCHEMA, model: 'sonnet' }),
  (review, unit) => {
    const findings = (review && review.findings) || []
    if (findings.length === 0) return { unit: unit.name, confirmed: [] }
    return agent(verifyPrompt(unit.name, findings), { label: `verify:${unit.name}`, phase: 'Verify', schema: VERIFY_SCHEMA, model: 'sonnet' })
  }
)

const confirmed = results.filter(Boolean).flatMap((r) => (r && r.confirmed) || [])
log(`${confirmed.length} confirmed findings after verification`)

phase('Synthesize')
const synth = await agent(synthPrompt(confirmed), { label: 'synthesize', phase: 'Synthesize', schema: SYNTH_SCHEMA })

return {
  totalConfirmed: synth.totalConfirmed,
  byPrinciple: synth.byPrinciple,
  bySeverity: synth.bySeverity,
  reportMarkdown: synth.reportMarkdown,
  confirmed,
}
