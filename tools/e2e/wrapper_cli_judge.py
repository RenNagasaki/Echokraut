"""B10 judge (plan docs/plans/ingame-e2e.md): reads the files wrapper-cli.ps1 left per case and checks them
against the wrapper's own i18n catalog. Mac side, no wrapper code is changed or run beyond the catalog import.

Usage: python3 -I wrapper_cli_judge.py <out-dir> <echokrautts-repo> <case>...
Prints one "PASS/FAIL/INFO <case>: <text>" line per check; exit 1 when any check fails.
"""
from __future__ import annotations

import importlib.util
import json
import re
import string
import sys
from pathlib import Path


def load_catalog(repo: Path) -> dict[str, dict[str, str]]:
    spec = importlib.util.spec_from_file_location("ek_i18n", repo / "wrapper" / "src" / "i18n.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.MESSAGES


def template_regex(template: str) -> re.Pattern[str]:
    parts = []
    for literal, name, _, _ in string.Formatter().parse(template):
        parts.append(re.escape(literal))
        if name is not None:
            parts.append("(?P<%s>.*?)" % name if name.isidentifier() else ".*?")
    return re.compile("^" + "".join(parts) + "$", re.DOTALL)


def read_events(path: Path) -> tuple[list[dict], list[str]]:
    events, junk = [], []
    raw = path.read_bytes().decode("utf-8", errors="replace")
    for line in raw.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            events.append(json.loads(line))
        except json.JSONDecodeError:
            junk.append(line)
    return events, junk


def first_fatal(events: list[dict]) -> tuple[list[dict], dict]:
    fatal = [e for e in events if e.get("event") == "error" and e.get("fatal")]
    return fatal, (fatal[0] if fatal else {})


class Judge:
    def __init__(self, catalog: dict[str, dict[str, str]]):
        self.catalog = catalog
        self.failed = False

    def report(self, ok: bool | None, case: str, text: str) -> None:
        tag = "INFO" if ok is None else ("PASS" if ok else "FAIL")
        if ok is False:
            self.failed = True
        print(f"{tag} {case}: {text}")

    def keyed(self, events: list[dict], key: str) -> list[dict]:
        return [e for e in events if e.get("key") == key]

    def matches(self, event: dict, language: str) -> re.Match[str] | None:
        entry = self.catalog.get(event.get("key"), {})
        template = entry.get(language) or entry.get("en")
        return template_regex(template).match(str(event.get("message", ""))) if template else None

    def language_and_keys(self, case: str, events: list[dict], junk: list[str], language: str) -> None:
        texts = [e for e in events if isinstance(e.get("message"), str)]
        keyed = [e for e in texts if e.get("key")]
        unknown = sorted({e["key"] for e in keyed if e["key"] not in self.catalog})
        wrong = [e for e in keyed if e["key"] in self.catalog and not self.matches(e, language)]
        unkeyed = [e["message"] for e in texts if not e.get("key")]
        mojibake = [e["message"] for e in texts if "�" in e["message"] or "Ã" in e["message"]]
        self.report(len(keyed) >= 3 and not wrong and not unknown, case,
                    f"{len(keyed)} keyed texts, all in '{language}'" if not wrong and not unknown else
                    f"{len(keyed)} keyed, {len(wrong)} not in '{language}': "
                    + "; ".join(f"{e['key']}={e['message']!r}" for e in wrong[:3]) + (f", unknown keys {unknown}" if unknown else ""))
        self.report(not mojibake and not junk, case,
                    "no mojibake, every stdout line is JSON" if not mojibake and not junk else
                    f"mojibake {mojibake[:2]!r}, non-JSON lines {junk[:2]!r}")
        if unkeyed:
            self.report(None, case, f"{len(unkeyed)} texts without key: " + "; ".join(repr(m[:90]) for m in unkeyed[:4]))

    def no_download(self, case: str, result: dict) -> None:
        new_models = result.get("newModelFiles") or []
        self.report(not new_models, case, "no model file written" if not new_models else f"model files written: {new_models[:5]}")

    def pocket6_fr(self, case: str, events: list[dict], result: dict) -> None:
        fallback = self.keyed(events, "pocket.layers_fallback")
        m = self.matches(fallback[0], "fr") if fallback else None
        ok = bool(m) and m.group("wanted") == "6" and m.group("size") == "24" and m.group("code") == "'fr'"
        self.report(ok, case, f"pocket.layers_fallback 6 -> 24 for 'fr': {fallback[0]['message']!r}" if fallback else
                    "no pocket.layers_fallback event")
        health = result.get("health") or {}
        self.report(result.get("ready") and health.get("tts_backend") == "pocket", case,
                    f"ready, /health tts_backend={health.get('tts_backend')} device={health.get('device')}, {result.get('secondsToEnd')} s")
        tts = result.get("tts") or {}
        self.report(bool(tts.get("ok")) and tts.get("audioSeconds", 0) >= 0.5, case,
                    f"/tts language fr: {tts.get('audioSeconds')} s audio in {tts.get('seconds')} s" if tts.get("ok") else
                    f"/tts language fr failed: {tts.get('error')}")
        shutdown = any(e.get("event") == "shutdown" for e in events)
        self.report(shutdown and result.get("exitCode") == 0, case,
                    f"shutdown event, exit {result.get('exitCode')}" if shutdown else "no shutdown event")

    def no_late_steps(self, case: str, events: list[dict]) -> None:
        late = [e for e in events if e.get("event") == "progress" and e.get("step") in ("deps", "model")]
        self.report(not late, case, "no deps/model step started" if not late else f"steps started: {sorted({e['step'] for e in late})}")

    def pocket_ja(self, case: str, events: list[dict], result: dict) -> None:
        fatal, first = first_fatal(events)
        msg = str(first.get("message", ""))
        m = self.matches(first, "ja") if first.get("key") == "boot.unsupported_language" else None
        clear = bool(m) and m.group("code") == "'ja'" and "de" in m.group("available")
        self.report(clear and not result.get("ready") and result.get("exitCode") != 0, case,
                    f"stops with {first.get('key')}: {msg!r}, exit {result.get('exitCode')}" if fatal else
                    f"no fatal error event (ready={result.get('ready')}, exit {result.get('exitCode')})")
        self.no_late_steps(case, events)
        self.no_download(case, result)
        grew = {k: v for k, v in (result.get("venvsAfter") or {}).items() if (result.get("venvsBefore") or {}).get(k) != v}
        self.report(not grew, case, "no venv installed or changed" if not grew else
                    "venv installed before the language was refused: " + ", ".join(f"{k} {v / 1e9:.1f} GB" for k, v in grew.items()))

    def qwen_cpu(self, case: str, events: list[dict], result: dict) -> None:
        fatal, first = first_fatal(events)
        m = self.matches({**first, "key": "boot.unsupported_device"}, "de") if fatal else None
        self.report(bool(m) and m.group("backend") == "cpu" and result.get("exitCode") != 0, case,
                    f"stops with the boot.unsupported_device text: {first.get('message')!r}, exit {result.get('exitCode')}" if fatal else
                    f"no fatal error event (ready={result.get('ready')}, exit {result.get('exitCode')})")
        if fatal:
            self.report(first.get("key") == "boot.unsupported_device", case,
                        "fatal error carries key boot.unsupported_device" if first.get("key") else
                        "fatal error has no key (bootstrap FatalError(i18n.t(...)) -> str(exc) drops the catalog key)")
        self.no_late_steps(case, events)
        self.no_download(case, result)
        qwen_venvs = [k for k in (result.get("venvsAfter") or {}) if "qwen" in k]
        self.report(not qwen_venvs, case, "no Qwen venv" if not qwen_venvs else f"Qwen venv created: {qwen_venvs}")


CASES = {
    "pocket6-fr": ("fr", Judge.pocket6_fr),
    "pocket-ja": ("ja", Judge.pocket_ja),
    "qwen-cpu": ("de", Judge.qwen_cpu),
}


def main(argv: list[str]) -> int:
    out, repo, cases = Path(argv[1]), Path(argv[2]), argv[3:]
    judge = Judge(load_catalog(repo))
    for case in cases:
        ndjson, result_file = out / f"{case}.ndjson", out / f"{case}.result.json"
        if not ndjson.exists() or not result_file.exists():
            judge.report(False, case, "no output fetched")
            continue
        events, junk = read_events(ndjson)
        result = json.loads(result_file.read_text(encoding="utf-8-sig"))
        language, check = CASES[case]
        judge.language_and_keys(case, events, junk, language)
        check(judge, case, events, result)
    return 1 if judge.failed else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
