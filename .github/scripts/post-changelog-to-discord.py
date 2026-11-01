#!/usr/bin/env python3
"""
Posts an Echokraut release changelog to Discord as colored embeds.

Reads Echokraut/Resources/Changelogs/v{TAG}_{LANG}.txt, parses its plain-text
section structure (header banner + MAJOR FEATURES / IMPROVEMENTS / BUG FIXES),
converts the body to Discord-flavored Markdown, and posts one embed per major
section. Each embed has a section-specific color so the channel scrollback
reads as a structured release post instead of a wall of mono-spaced text.

Env vars (injected by .github/workflows/github-releases-to-discord.yml):
  WEBHOOK    Discord webhook URL (empty → warning + exit 0)
  TAG        release tag (e.g. "0.19.0.0")
  LANG       "EN" or "DE"
  ROLE_PING  optional Discord role ID; prepended to the intro as <@&ID>

Behaviour on missing inputs:
  - WEBHOOK empty   → warning, exit 0 (workflow continues for the other lang)
  - changelog file missing → warning, exit 0 (release ships, post manually)
  - any HTTP non-2xx → error, exit 1
"""

import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path


# ── Inputs / defensive exits ──────────────────────────────────────────────

WEBHOOK = os.environ.get("WEBHOOK", "")
TAG = os.environ["TAG"]
LANG = os.environ["LANG"]
ROLE_PING = os.environ.get("ROLE_PING", "")

if not WEBHOOK:
    print(f"::warning::DISCORD_WEBHOOK_{LANG} secret not configured — skipping {LANG} announcement")
    sys.exit(0)

changelog_path = Path(f"Echokraut/Resources/Changelogs/v{TAG}_{LANG}.txt")
if not changelog_path.exists():
    print(f"::warning::Changelog file not found: {changelog_path} — skipping {LANG} announcement")
    sys.exit(0)


# ── Parse the plain-text source into (section_title, body_lines) tuples ──

# Major sections in the long-form changelog are framed by lines of all-equals
# chars. The very first such block holds the file header (release tag / commit /
# date) and is filtered out below — it's metadata, not a section the user wants
# to see as a Discord embed.
DIVIDER = re.compile(r"^=+$")
HEADER_KEYWORDS = ("CHANGELOG", "RELEASE-TAG", "RELEASE TAG")

# utf-8-sig, NOT utf-8: editors on Windows save these files with a UTF-8 BOM
# often enough that it has to be tolerated. With plain "utf-8" the BOM survives
# as ﻿ on the first line, the opening "====" divider no longer matches
# ^=+$, and the divider-pair walk below desynchronizes by one block — section
# bodies become titles. Discord then rejects the payload with HTTP 400
# ("embeds": ["1"]) because a title exceeds 256 chars. Happened for v0.19.3.0.
raw_text = changelog_path.read_text(encoding="utf-8-sig")
lines = raw_text.splitlines()

sections = []
i = 0
while i < len(lines):
    if not DIVIDER.match(lines[i]):
        i += 1
        continue
    # Found opening divider — collect title between this and the next divider.
    i += 1
    title_buf = []
    while i < len(lines) and not DIVIDER.match(lines[i]):
        title_buf.append(lines[i])
        i += 1
    title = "\n".join(t.strip() for t in title_buf).strip()
    # Skip the closing divider.
    if i < len(lines) and DIVIDER.match(lines[i]):
        i += 1
    # Body runs until the next opening divider.
    body_buf = []
    while i < len(lines) and not DIVIDER.match(lines[i]):
        body_buf.append(lines[i])
        i += 1
    sections.append((title, body_buf))

# Drop the file header (the "ECHOKRAUT CHANGELOG / Vom Release-Tag ..." block).
sections = [
    (title, body) for title, body in sections
    if not any(k in title.upper() for k in HEADER_KEYWORDS)
]


# ── Plain-text → Discord Markdown conversion ──────────────────────────────

# Discord embed descriptions support **bold**, *italic*, `code`, [link](url),
# bullet lists (- / •), and blockquotes (>) — but NOT # / ## / ### headers.
# So [NEW]-style markers become bold subtitle lines rather than Markdown
# headers, which would render as plain "## " text in the embed body.

FEATURE_MARKER = re.compile(r"^\[(NEW|NEU|UI|FIX|BUG)\]\s+(.+)$")


def to_markdown(body_lines):
    """Convert one section's body lines into Discord-friendly Markdown."""
    out = []
    pending_para = []   # 2-space-indented paragraph lines waiting to be joined
    last_kind = None    # "bullet" | "feature" | None — drives continuation behavior

    def flush_para():
        nonlocal pending_para
        if pending_para:
            out.append(" ".join(line.strip() for line in pending_para))
            pending_para = []

    for raw in body_lines:
        line = raw.rstrip()

        if not line:
            flush_para()
            # Preserve blank-line section breaks so distinct [NEW] entries stay
            # visually separated in the rendered embed.
            if out and out[-1] != "":
                out.append("")
            last_kind = None
            continue

        # [NEW]/[NEU]/[UI]/[FIX] marker — promote to bold subtitle line.
        # The bracketed tag is dropped from the rendered output; the section
        # color (green/blue/orange embed sidebar) and the structural position
        # already convey "this is a feature/improvement/fix", so a literal
        # "NEW:" prefix would just add visual clutter.
        m = FEATURE_MARKER.match(line)
        if m:
            flush_para()
            out.append(f"**{m.group(2)}**")
            last_kind = "feature"
            continue

        # Bullet list item — appears in two indentations in the source:
        #   "- ..."   at col 0 (used by BUG FIXES / IMPROVEMENTS sub-bullets)
        #   "  - ..." at col 2 (used inside a [NEW] feature description block)
        # Both render the same way after conversion.
        m_bullet = re.match(r"^(?:  )?- (.+)$", line)
        if m_bullet:
            flush_para()
            out.append(f"• {m_bullet.group(1).strip()}")
            last_kind = "bullet"
            continue

        # 2-space-indented continuation:
        #  - after a bullet → fold onto the previous bullet line
        #  - after a feature/paragraph → buffer until next non-continuation
        if line.startswith("  "):
            stripped = line.strip()
            if last_kind == "bullet" and out:
                out[-1] += " " + stripped
            else:
                pending_para.append(line)
            continue

        # Anything else (top-level prose) — flush + emit verbatim.
        flush_para()
        out.append(line)
        last_kind = None

    flush_para()
    return "\n".join(out).strip()


# ── Build one embed per section, color per kind ───────────────────────────

# Color picks: green = additive, blue = polish, orange = recovery. Same color
# applies to the DE counterpart of each section so a German reader sees the
# identical visual structure. Color alone carries the semantic — no emojis,
# the embed sidebar bar already does the visual lift.
# The DE files have used several wordings for the same section over the
# releases (GROSSE NEUERUNGEN / NEUE HAUPTFEATURES / NEUE HAUPTFUNKTIONEN,
# BUGFIXES / FEHLERBEHEBUNGEN). All variants that actually occur in
# Resources/Changelogs are mapped, so a German reader gets the same colors as
# an English one instead of the purple fallback.
SECTION_COLORS = {
    "MAJOR NEW FEATURES":   0x4CAF50,
    "NEUE HAUPTFEATURES":   0x4CAF50,
    "NEUE HAUPTFUNKTIONEN": 0x4CAF50,
    "GROSSE NEUERUNGEN":    0x4CAF50,
    "IMPROVEMENTS":         0x2196F3,
    "VERBESSERUNGEN":       0x2196F3,
    "BUG FIXES":            0xFF9800,
    "BUGFIXES":             0xFF9800,
    "FEHLERBEHEBUNGEN":     0xFF9800,
}
DEFAULT_COLOR = 0x9C27B0  # purple — for unknown section names

# Discord caps embed descriptions at 4096 chars. We split on paragraph
# (\n\n) boundaries when a section exceeds that, attaching "(part N/M)" to
# the title so the reader sees they're still inside the same section.
DESC_LIMIT = 4096

# Discord caps embed titles at 256 chars and rejects the whole message with
# HTTP 400 if one is longer. A section title comes straight from the source
# file, so a malformed changelog can produce an oversized one — truncate rather
# than let a formatting slip block the whole announcement.
TITLE_LIMIT = 256


def split_description(desc):
    if len(desc) <= DESC_LIMIT:
        return [desc]
    chunks, current = [], ""
    for para in desc.split("\n\n"):
        # A single paragraph can exceed the limit on its own (one very long
        # unbroken entry). Hard-split it first, otherwise it would be emitted
        # oversized and rejected.
        while len(para) > DESC_LIMIT:
            if current:
                chunks.append(current)
                current = ""
            chunks.append(para[:DESC_LIMIT])
            para = para[DESC_LIMIT:]
        candidate = (current + ("\n\n" if current else "") + para)
        if len(candidate) > DESC_LIMIT and current:
            chunks.append(current)
            current = para
        else:
            current = candidate
    if current:
        chunks.append(current)
    return chunks


def clamp_title(title):
    if len(title) <= TITLE_LIMIT:
        return title
    print(f"::warning::Section title exceeds {TITLE_LIMIT} chars, truncating: {title[:80]!r}...")
    return title[:TITLE_LIMIT - 1] + "…"


embeds = []
for title, body_lines in sections:
    color = SECTION_COLORS.get(title.upper(), DEFAULT_COLOR)
    description = to_markdown(body_lines)
    if not description:
        continue

    parts = split_description(description)
    for idx, part in enumerate(parts):
        suffix = f" (part {idx + 1}/{len(parts)})" if len(parts) > 1 else ""
        embeds.append({
            "title": clamp_title(f"{title}{suffix}"),
            "description": part,
            "color": color,
        })

if not embeds:
    print(f"::warning::Changelog parsed empty for {LANG} v{TAG} — nothing to post")
    sys.exit(0)


# ── Compose intro + post (max 10 embeds per webhook payload) ──────────────

if LANG == "DE":
    intro = f"**Echokraut v{TAG} veröffentlicht!**"
elif LANG == "EN":
    intro = f"**Echokraut v{TAG} released!**"
else:
    intro = f"**Echokraut v{TAG}**"

if ROLE_PING:
    intro = f"<@&{ROLE_PING}> {intro}"


def post(payload):
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        WEBHOOK,
        data=body,
        headers={
            "Content-Type": "application/json",
            # Discord's Cloudflare layer rejects Python's default
            # "Python-urllib/X.Y" UA with HTTP 403 + Cloudflare error 1010
            # (browser signature ban). A descriptive UA matching Discord's
            # API user-agent guideline (<product> (<url>, <version>)) gets
            # let through.
            "User-Agent": "Echokraut-ReleaseAnnouncer/1.0 (+https://github.com/RenNagasaki/Echokraut)",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req) as resp:
            # Discord usually returns 204 No Content on a successful webhook send.
            if resp.status not in (200, 204):
                print(f"::error::Discord webhook returned HTTP {resp.status}: {resp.read().decode('utf-8', errors='replace')}")
                sys.exit(1)
    except urllib.error.HTTPError as e:
        print(f"::error::Discord webhook returned HTTP {e.code}: {e.read().decode('utf-8', errors='replace')}")
        sys.exit(1)


# Discord enforces TWO caps per webhook message:
#   1. Max 10 embeds per payload (hard reject otherwise).
#   2. Sum of every char across every embed (title + description, plus
#      author/footer/fields if used) MUST be ≤ 6000. A real changelog with
#      4 sections at ~2-3k chars each clears this easily, so we batch by
#      both count AND cumulative size.
EMBEDS_PER_MSG = 10
TOTAL_CHAR_LIMIT = 6000

def embed_size(emb):
    # Only title + description are populated by this script. If we ever add
    # fields/footer/author, factor those in here too.
    return len(emb.get("title", "")) + len(emb.get("description", ""))

batches = []
current = []
current_size = 0
for emb in embeds:
    size = embed_size(emb)
    overflows = (
        len(current) >= EMBEDS_PER_MSG
        or (current and current_size + size > TOTAL_CHAR_LIMIT)
    )
    if overflows:
        batches.append(current)
        current = [emb]
        current_size = size
    else:
        current.append(emb)
        current_size += size
if current:
    batches.append(current)

for idx, batch in enumerate(batches):
    payload = {"embeds": batch}
    if idx == 0:
        # Plain content goes ABOVE the embed cards; intro only fires once.
        payload["content"] = intro
    post(payload)
    # Tiny pause between messages to keep clients rendering them in order
    # and to avoid brushing Discord's per-webhook rate limit (~30/min).
    time.sleep(1)

print(f"Posted {len(embeds)} embed(s) in {len(batches)} message(s) to {LANG} Discord webhook for tag {TAG}")
