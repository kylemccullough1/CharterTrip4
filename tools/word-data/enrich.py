#!/usr/bin/env python3
"""
Fill the word bank's definitions, parts of speech and example sentences.

The bee's host phone has to answer three things a speller is allowed to ask — what it means,
what part of speech it is, and it used in a sentence — and the Scripps list is words only. This
walks the bank and asks two free, key-less sources for the rest:

  * Datamuse  (api.datamuse.com)          definitions + part of speech. Near-total coverage.
  * Wiktionary (en.wiktionary.org REST)   example sentences, where anybody has written one.

It writes back into src/CharterTrip.Core/Words/word-bank.json in place, entry by entry, so it is
safe to stop and restart: anything already enriched is skipped. That matters because this is
~3,850 words and the venue has no internet — the data ships in the repo, and this is the only
thing that ever needs a connection.

Usage:
    python3 tools/word-data/enrich.py [--limit N] [--tier KEY] [--workers N]
"""

import argparse, html, json, os, re, ssl, threading, time
from concurrent.futures import ThreadPoolExecutor
from urllib.parse import quote
from urllib.request import Request, urlopen
from urllib.error import URLError, HTTPError

# python.org builds on macOS ship without a certificate store wired up, so HTTPS fails here even
# though curl to the same host works. certifi is the usual answer; if it is not installed, fall
# back to the system defaults and let a failure be a failure rather than turning verification off.
try:
    import certifi
    SSL_CTX = ssl.create_default_context(cafile=certifi.where())
except ImportError:
    SSL_CTX = ssl.create_default_context()

BANK = os.path.join(os.path.dirname(__file__), "..", "..",
                    "src", "CharterTrip.Core", "Words", "word-bank.json")

TIERS = ["easy", "easyModerate", "moderate", "moderatelyDifficult", "difficult", "expert"]

# Datamuse tags its definitions with a one-letter part of speech.
POS = {"n": "noun", "v": "verb", "adj": "adjective", "adv": "adverb", "u": ""}

UA = {"User-Agent": "CharterTrip-bee/1.0 (spelling bee word data; local use)"}

_print_lock = threading.Lock()


class RateLimited(Exception):
    """The server asked us to slow down. Worth telling apart from a word simply not being there."""


def get(url, timeout=12):
    try:
        with urlopen(Request(url, headers=UA), timeout=timeout, context=SSL_CTX) as r:
            return json.loads(r.read().decode("utf-8"))
    except HTTPError as e:
        if e.code == 429:
            raise RateLimited() from e
        return None
    except (URLError, TimeoutError, json.JSONDecodeError, OSError):
        return None


def strip_tags(s):
    return html.unescape(re.sub(r"<[^>]+>", "", s or "")).strip()


def clean_definition(text, word):
    """One sentence, no leading gloss labels, and never the word itself giving the game away."""
    text = strip_tags(text)
    text = re.sub(r"^\((?:[^)]*)\)\s*", "", text)          # drop "(gambling)" style labels
    text = re.sub(r"\s+", " ", text).strip(" ;:,.")
    if not text:
        return ""
    # A definition that contains the word is useless to a speller who is trying to spell it.
    if re.search(rf"\b{re.escape(word)}\b", text, re.I):
        return ""
    if len(text) > 180:
        text = text[:177].rsplit(" ", 1)[0] + "…"
    return text[0].upper() + text[1:]


def from_datamuse(word):
    data = get(f"https://api.datamuse.com/words?sp={quote(word)}&md=dp&max=1")
    if not data or data[0].get("word", "").lower() != word.lower():
        return "", ""

    entry = data[0]
    pos = ""
    for tag in entry.get("tags", []):
        if tag in POS and POS[tag]:
            pos = POS[tag]
            break

    for raw in entry.get("defs", []):
        tag, _, body = raw.partition("\t")
        definition = clean_definition(body, word)
        if definition:
            return definition, POS.get(tag, "") or pos
    return "", pos


def from_tatoeba(word):
    """A real sentence somebody wrote, from the Tatoeba corpus.

    Preferred over Wiktionary's citations for this job: a bee wants "use it in a sentence" to
    sound like speech, not like a quotation from 1834 — and Tatoeba tolerates being asked 3,850
    times, which the Wiktionary REST endpoint firmly does not."""
    data = get(f"https://tatoeba.org/en/api_v0/search?from=eng&query={quote(word)}&limit=6")
    if not isinstance(data, dict):
        return ""

    best = ""
    for row in (data.get("results") or []):
        text = (row.get("text") or "").strip()
        # It has to actually contain the word, and be short enough to read aloud once.
        if not re.search(rf"\b{re.escape(word)}\w*\b", text, re.I):
            continue
        if not (20 <= len(text) <= 140):
            continue
        if not best or len(text) < len(best):
            best = text
    return best


def from_wiktionary(word):
    """A definition and, more importantly, a sentence somebody actually wrote."""
    data = get(f"https://en.wiktionary.org/api/rest_v1/page/definition/{quote(word)}")
    if not isinstance(data, dict):
        return "", "", ""

    for section in data.get("en", []):
        pos = (section.get("partOfSpeech") or "").lower()
        for d in section.get("definitions", []):
            definition = clean_definition(d.get("definition", ""), word)
            sentence = ""
            for ex in d.get("parsedExamples", []) or []:
                sentence = strip_tags(ex.get("example", ""))
                if sentence:
                    break
            if not sentence:
                for ex in d.get("examples", []) or []:
                    sentence = strip_tags(ex)
                    if sentence:
                        break
            if len(sentence) > 200:
                sentence = ""
            if definition or sentence:
                return definition, pos, sentence
    return "", "", ""


CHECKED = os.path.join(os.path.dirname(__file__), "sentence-checked.json")


def load_checked():
    """Words already asked about for a sentence, so a re-run does not ask again.

    Kept beside the tool rather than in the bank because "we looked and there wasn't one" is a
    fact about this script's progress, not about the word."""
    try:
        return set(json.load(open(CHECKED, encoding="utf-8")))
    except (OSError, json.JSONDecodeError):
        return set()


def save_checked(words):
    json.dump(sorted(words), open(CHECKED, "w", encoding="utf-8"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--limit", type=int, default=0, help="stop after this many words")
    ap.add_argument("--tier", action="append", help="only this tier (repeatable)")
    ap.add_argument("--workers", type=int, default=6)
    ap.add_argument("--delay", type=float, default=0.05, help="pause per word, per worker")
    ap.add_argument("--pass", dest="pass_", choices=["def", "sentence"], default="def",
                    help="def: definitions + part of speech (Datamuse). "
                         "sentence: example sentences (Wiktionary, rate limited).")
    args = ap.parse_args()

    path = os.path.abspath(BANK)
    bank = json.load(open(path, encoding="utf-8"))

    # Normalise: the file may hold bare strings, objects, or a mix mid-run.
    for tier in TIERS:
        bank[tier] = [e if isinstance(e, dict) else {"word": e} for e in bank.get(tier, [])]

    checked = load_checked()

    todo = []
    for tier in (args.tier or TIERS):
        for i, e in enumerate(bank.get(tier, [])):
            if args.pass_ == "def" and not e.get("definition"):
                todo.append((tier, i, e["word"]))
            elif args.pass_ == "sentence" and not e.get("sentence") and e["word"] not in checked:
                todo.append((tier, i, e["word"]))

    if args.limit:
        todo = todo[: args.limit]

    total = len(todo)
    print(f"{args.pass_} pass: {total} words to look up "
          f"({sum(len(bank[t]) for t in TIERS)} in the bank)", flush=True)
    if not total:
        return

    done = [0]
    hits = [0]
    stop = threading.Event()

    def work(item):
        tier, i, word = item
        if stop.is_set():
            return

        try:
            if args.pass_ == "def":
                definition, pos = from_datamuse(word)
                with _print_lock:
                    entry = bank[tier][i]
                    entry["word"] = word
                    if definition:
                        entry["definition"] = definition
                        hits[0] += 1
                    if pos:
                        entry["partOfSpeech"] = pos
            else:
                sentence = from_tatoeba(word)
                if not sentence:
                    _, _, sentence = from_wiktionary(word)
                with _print_lock:
                    checked.add(word)
                    if sentence:
                        bank[tier][i]["sentence"] = sentence
                        hits[0] += 1
        except RateLimited:
            # Back off rather than burning through the rest of the list getting refused. The run
            # is resumable, so stopping early costs nothing but the next invocation.
            stop.set()
            return

        with _print_lock:
            done[0] += 1
            if done[0] % 25 == 0 or done[0] == total:
                print(f"  {done[0]}/{total}  got {hits[0]}", flush=True)
                save(path, bank)
                if args.pass_ == "sentence":
                    save_checked(checked)

        time.sleep(args.delay)

    with ThreadPoolExecutor(max_workers=args.workers) as pool:
        list(pool.map(work, todo))

    save(path, bank)
    if args.pass_ == "sentence":
        save_checked(checked)

    if stop.is_set():
        print("\n  stopped early: the server started refusing requests. "
              "Wait a few minutes and run it again — it picks up where it left off.")

    with_def = sum(1 for t in TIERS for e in bank[t] if e.get("definition"))
    with_sen = sum(1 for t in TIERS for e in bank[t] if e.get("sentence"))
    n = sum(len(bank[t]) for t in TIERS)
    print(f"\ndone. {with_def}/{n} have a definition, {with_sen}/{n} have a sentence.")


def save(path, bank):
    """One tier per line, so the file still reads in a diff."""
    out = ["{"]
    for i, tier in enumerate(TIERS):
        comma = "," if i < len(TIERS) - 1 else ""
        out.append(f'  "{tier}": {json.dumps(bank[tier], ensure_ascii=False)}{comma}')
    out.append("}")
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")
    os.replace(tmp, path)


if __name__ == "__main__":
    main()
