"""Builds and checks build/vMenu/config/clothing-layouts.json.

vMenu saves clothing by collection (collection name, drawable index inside it, texture). This file
gives the client two things it can't work out alone:

  legacy  The captured pre-change global layout. Saves made before collection references only have
          global numbers; they are read with this baseline, never with whatever layout is running.
  pack    The PSRP pack revisions (texture counts per drawable of each PSRP collection) and the
          verified remaps between them, made from the EUP builder's current-to-new.csv.

Commands (paths are arguments; nothing is read from the current directory):

  revision  --layouts FILE --id ID --ymt XML [--ymt XML ...] [--notes TEXT] [--current]
            Add a pack revision from CodeWalker YMT XML exports of the pack's collections.
  baseline  --layouts FILE --capture FILE --id ID --valid-from DATE
            Validate an in-game capture (vmenu_clothing_capture) and make it the legacy baseline.
            Its PSRP collections must match a declared revision exactly; that becomes legacy.rev.
  remap     --layouts FILE --from REV --to REV --map current-to-new.csv [--review comparison.json]
            Add the verified step between two revisions. Every drawable/texture of the old
            revision's collections must be accounted for; anything the map doesn't cover is
            written as "review", never assumed unchanged.
  check     --layouts FILE [--capture FILE]
            Validate the file; with a capture, say whether that server matches it.
  print     --capture FILE
            Print a capture's layout and pack prints (as vMenu computes them).

The prints mirror ClothingPrints in vMenu/ClothingRefs.cs (FNV-1a 64 over a canonical string).
"""

import argparse
import csv
import datetime
import hashlib
import json
import os
import sys
import xml.etree.ElementTree as ET

MALE = 0x705E61F2    # mp_m_freemode_01
FEMALE = 0x9C9EFFD8  # mp_f_freemode_01
MODELS = {"mp_m_freemode_01": MALE, "mp_f_freemode_01": FEMALE, "m": MALE, "f": FEMALE}

LAYOUT_KEYS = ["c%d" % i for i in range(12)] + ["p%d" % i for i in range(13)]
COMPONENTS = {"head": "c0", "berd": "c1", "hair": "c2", "uppr": "c3", "lowr": "c4", "hand": "c5", "feet": "c6",
              "teef": "c7", "accs": "c8", "task": "c9", "decl": "c10", "jbib": "c11",
              "p_head": "p0", "p_eyes": "p1", "p_ears": "p2", "p_lwrist": "p6", "p_rwrist": "p7"}


class Fail(Exception):
    pass


def fnv64(text):
    h = 0xcbf29ce484222325
    for b in text.encode("utf-8"):
        h ^= b
        h = (h * 0x100000001b3) & 0xFFFFFFFFFFFFFFFF
    return "%016x" % h


def layout_print(slots):
    """slots: key -> [[collection, start, count, textures], ...]"""
    parts = []
    for key in LAYOUT_KEYS:
        rows = sorted(slots.get(key) or [], key=lambda r: r[1])
        parts.append(key + "=" + ",".join("%s:%d" % (r[0] or "", r[2]) for r in rows if r[2] > 0))
    return fnv64("|".join(parts))


def pack_print(collections, counts):
    """counts(collection, key) -> list of texture counts per drawable"""
    parts = []
    for col in sorted(set(c for c in collections if c)):
        for key in LAYOUT_KEYS:
            values = counts(col, key) or []
            if values:
                parts.append("%s/%s=%s" % (col, key, ",".join(str(v) for v in values)))
    return fnv64("|".join(parts))


def load_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def save_json(path, data):
    text = json.dumps(data, indent=1, sort_keys=False, ensure_ascii=False)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text + "\n")


def empty_layouts():
    return {"version": 1, "legacy": {"id": None, "rev": None, "capturedAt": None, "validFrom": None, "validFromUnix": 0, "models": {}},
            "pack": {"current": None, "strict": True, "revisions": [], "steps": []}}


def read_layouts(path):
    if not os.path.exists(path):
        return empty_layouts()
    data = load_json(path)
    if data.get("version") != 1:
        raise Fail("unsupported layouts version")
    data.setdefault("legacy", empty_layouts()["legacy"])
    data.setdefault("pack", empty_layouts()["pack"])
    return data


# ---- YMT XML (CodeWalker export of CPedVariationInfo) ------------------------------------------

def ymt_counts(path):
    root = ET.parse(path).getroot()
    if root.tag != "CPedVariationInfo":
        raise Fail("%s is not a CPedVariationInfo export" % path)
    collection = root.get("name")
    base = os.path.basename(path)
    model = MALE if base.startswith("mp_m_freemode_01") else FEMALE if base.startswith("mp_f_freemode_01") else None
    if not collection or model is None:
        raise Fail("%s: can't tell the collection and freemode model" % path)
    counts = {}
    avail = [int(x) for x in (root.findtext("availComp") or "").split()]
    data = root.find("aComponentData3")
    items = list(data) if data is not None else []
    for comp, index in enumerate(avail):
        if index == 255:
            continue
        drawables = items[index].find("aDrawblData3")
        textures = [len(list(d.find("aTexData") if d.find("aTexData") is not None else [])) for d in (list(drawables) if drawables is not None else [])]
        if textures:
            counts["%s/c%d" % (collection, comp)] = textures
    props = root.find("propInfo/aPropMetaData")
    per_anchor = {}
    for item in (list(props) if props is not None else []):
        anchor = int(item.find("anchorId").get("value"))
        prop = int(item.find("propId").get("value"))
        tex = item.find("texData")
        per_anchor.setdefault(anchor, {})[prop] = len(list(tex)) if tex is not None else 0
    for anchor, by_prop in per_anchor.items():
        if sorted(by_prop) != list(range(len(by_prop))):
            raise Fail("%s: anchor %d prop ids are not 0..n-1" % (path, anchor))
        counts["%s/p%d" % (collection, anchor)] = [by_prop[i] for i in range(len(by_prop))]
    return model, collection, counts


def cmd_revision(args):
    layouts = read_layouts(args.layouts)
    revision = {"id": args.id, "notes": args.notes or "", "collections": [], "counts": {}, "ymtSha256": {}}
    for path in args.ymt:
        model, collection, counts = ymt_counts(path)
        if collection not in revision["collections"]:
            revision["collections"].append(collection)
        revision["counts"].setdefault(str(model), {}).update(counts)
        with open(path, "rb") as f:
            revision["ymtSha256"][collection] = hashlib.sha256(f.read()).hexdigest()
    revision["collections"].sort()
    for model, counts in revision["counts"].items():
        revision["print_" + model] = pack_print(revision["collections"], lambda c, k, m=counts: m.get("%s/%s" % (c, k)))
    pack = layouts["pack"]
    pack["revisions"] = [r for r in pack["revisions"] if r["id"] != args.id] + [revision]
    if args.current or not pack.get("current"):
        pack["current"] = args.id
    save_json(args.layouts, layouts)
    print("revision %s: %s" % (args.id, ", ".join(revision["collections"])))


# ---- Captures ----------------------------------------------------------------------------------

def validate_capture(capture):
    if capture.get("kind") != "vmenu-clothing-capture" or capture.get("version") != 1:
        raise Fail("not a vMenu clothing capture")
    models = capture.get("models") or {}
    for name, model in (("mp_m_freemode_01", MALE), ("mp_f_freemode_01", FEMALE)):
        body = models.get(str(model))
        if not isinstance(body, dict):
            raise Fail("capture has no %s" % name)
        anomalies = body.get("anomalies") or []
        if anomalies:
            raise Fail("%s capture has anomalies: %s" % (name, anomalies[:3]))
        slots = body.get("slots") or {}
        if isinstance(slots, list):
            slots = {}
        for key, rows in slots.items():
            if key not in LAYOUT_KEYS:
                raise Fail("%s: unknown slot %s" % (name, key))
            expected = 0
            for row in sorted(rows, key=lambda r: r[1]):
                col, start, count, textures = row[0], row[1], row[2], row[3]
                if start != expected or count != len(textures) or count <= 0:
                    raise Fail("%s %s: rows are not one continuous list at %s" % (name, key, start))
                expected = start + count
        if layout_print(slots) != body.get("layout"):
            raise Fail("%s: layout print doesn't match its rows" % name)
    return models


def capture_counts(body):
    slots = body.get("slots") or {}
    if isinstance(slots, list):
        slots = {}
    out = {}
    for key, rows in slots.items():
        for row in rows:
            out["%s/%s" % (row[0] or "", key)] = list(row[3])
    return out


def match_revision(layouts, model, counts):
    for revision in layouts["pack"]["revisions"]:
        expected = revision.get("counts", {}).get(str(model))
        if expected and all(counts.get(k) == v for k, v in expected.items()):
            return revision["id"]
    return None


def cmd_baseline(args):
    layouts = read_layouts(args.layouts)
    capture = load_json(args.capture)
    models = validate_capture(capture)
    revisions = set()
    for model in (MALE, FEMALE):
        rev = match_revision(layouts, model, capture_counts(models[str(model)]))
        revisions.add(rev)
    if len(revisions) != 1 or None in revisions:
        raise Fail("the captured PSRP collections don't exactly match one declared pack revision (got %s). "
                   "Add the revision of the pack that server runs first." % sorted(map(str, revisions)))
    rev = revisions.pop()
    valid = datetime.datetime.fromisoformat(args.valid_from.replace("Z", "+00:00"))
    if valid.tzinfo is None:
        valid = valid.replace(tzinfo=datetime.timezone.utc)
    legacy = {"id": args.id, "rev": rev, "capturedAt": capture.get("capturedAt"), "validFrom": args.valid_from,
              "validFromUnix": int(valid.timestamp()), "gameBuild": capture.get("gameBuild"),
              "serverResources": capture.get("serverResources") or capture.get("clientResources") or [], "models": {}}
    for name, model in (("mp_m_freemode_01", MALE), ("mp_f_freemode_01", FEMALE)):
        body = models[str(model)]
        legacy["models"][str(model)] = {"name": name, "layout": body["layout"], "slots": body.get("slots") or {}}
    layouts["legacy"] = legacy
    save_json(args.layouts, layouts)
    print("baseline %s: pack %s, male %s, female %s" % (args.id, rev, legacy["models"][str(MALE)]["layout"], legacy["models"][str(FEMALE)]["layout"]))


# ---- Remaps from the EUP builder ---------------------------------------------------------------

def runtime_collection(name, prop=False):
    """FiveM names a collection by the shop meta's dlcName. Reports often use the fullDlcName
    (<ped name>_<dlcName>, e.g. mp_m_freemode_01_fibeup), or for props the file prefix
    (<ped name>_p_<dlcName>); strip those. Only names with the ped prefix are changed."""
    for ped in ("mp_m_freemode_01_", "mp_f_freemode_01_"):
        if name and name.startswith(ped):
            rest = name[len(ped):]
            return rest[2:] if prop and rest.startswith("p_") else rest
    return name


def slot_of(component):
    key = COMPONENTS.get(component)
    if not key:
        raise Fail("unknown component %s" % component)
    return key


def merge_target(targets, key, value, strict):
    previous = targets.get(key)
    if previous is None or previous == value:
        targets[key] = value
    elif previous == "retired":
        targets[key] = value   # another owner keeps this exact texture
    elif value == "retired":
        pass
    elif strict:
        raise Fail("%s maps to both %s and %s" % (key, previous, value))
    else:
        targets[key] = ("review", "the new build splits this texture between %s and %s" % (previous, value))


def read_current_to_new(path):
    """The builder's BUILD_REPORT/current-to-new.csv. It lists department choices only: textures it
    doesn't list become "review"."""
    targets, mapped = {}, set()
    with open(path, encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            model = MODELS[row["sex"]]
            mapped.add((model, row["collection"]))
            key = (model, slot_of(row["component"]), row["collection"], int(row["old_drawable"]), int(row["old_texture"]))
            status = row["status"].strip().lower()
            if status in ("retained", "moved", "renumbered"):
                value = (row.get("new_collection") or row["collection"], int(row["new_drawable"]), int(row["new_texture"]))
            elif status in ("removed", "retired", "disabled"):
                value = "retired"
            else:
                raise Fail("unknown status %s" % status)
            merge_target(targets, key, value, strict=True)
    return targets, mapped


def original_key(row):
    """A choice's permanent identity: the original pack item it came from. The builder keeps every
    original choice across builds and records where each one lands (BUILD_REPORT/original-to-new.csv)."""
    return (row["source_pack"], row["sex"], row["old_collection"], row.get("old_component") or row["component"],
            row["old_drawable"], row["old_texture"], (row.get("old_ytd") or "").replace("/", "\\").lower())


def read_registry(path):
    """Numbering/current-addon-registry.csv: (collection, component, drawable) -> (owners, texture count)."""
    out = {}
    with open(path, encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            owners = set(o.strip() for o in row["owners"].replace(";", ",").split(",") if o.strip())
            out[(row["collection"], row["component"], int(row["current_drawable"]))] = (owners, int(row["texture_count"]))
    return out


def compose_originals(old_path, new_path, old_registry, new_registry, rejected):
    """Two builds' original-to-new.csv joined on the original choice: where every current texture of
    the old build lands in the new one. Complete for every texture an original choice reaches.
    Each row is checked against that build's registry: its department must own the model at the
    place it claims, and the texture must exist. Rows that fail (e.g. STAFF_EUP rows recording
    mp_m_deptmerge2 for items that live in mp_m_leoaccessories) are left out and counted."""
    def load(path, registry, which):
        out = {}
        with open(path, encoding="utf-8-sig", newline="") as f:
            for row in csv.DictReader(f):
                place = None
                if row["status"].strip().lower() not in ("removed", "retired", "disabled") and row["new_drawable"] != "":
                    owned = registry.get((row["new_collection"], row["component"], int(row["new_drawable"])))
                    if not owned or row["department"] not in owned[0] or int(row["new_texture"]) >= owned[1]:
                        rejected[(which, row["department"], row["new_collection"])] = rejected.get((which, row["department"], row["new_collection"]), 0) + 1
                        continue
                    place = (MODELS[row["sex"]], slot_of(row["component"]), row["new_collection"], int(row["new_drawable"]), int(row["new_texture"]))
                out.setdefault(original_key(row), set()).add(place)
        return out
    before = load(old_path, read_registry(old_registry), "old")
    after = load(new_path, read_registry(new_registry), "new")
    targets, mapped = {}, set()
    for key, places in before.items():
        landing = after.get(key)
        for place in places:
            if place is None:
                continue
            mapped.add((place[0], place[2]))
            if not landing or landing == {None}:
                merge_target(targets, place, "retired", strict=False)
                continue
            kept = sorted(p for p in landing if p is not None)
            if len(kept) == 1:
                value = kept[0]
            elif len(places) == len(kept):
                # The same choice in several places in both builds (a compatibility copy): pair in order.
                value = kept[sorted(places).index(place)]
            else:
                value = ("review", "the new build places this choice in %d slots" % len(kept))
            if isinstance(value, tuple) and value[0] != "review":
                value = (value[2], value[3], value[4])
            merge_target(targets, place, value, strict=False)
    return targets, mapped


def cmd_remap(args):
    layouts = read_layouts(args.layouts)
    revisions = {r["id"]: r for r in layouts["pack"]["revisions"]}
    if args.__dict__["from"] not in revisions or args.to not in revisions:
        raise Fail("both revisions must be declared first")
    old = revisions[args.__dict__["from"]]
    new = revisions[args.to]
    mapped = set()   # (model, collection) the builder's map covers
    targets = {}     # (model, slot, col, drw, tex) -> (toCol, toDrw, toTex) | "retired" | ("review", why)
    rejected = {}
    if args.originals:
        if not args.registries:
            raise Fail("--originals needs --registries (both builds' Numbering/current-addon-registry.csv) to check each row")
        targets, mapped = compose_originals(args.originals[0], args.originals[1], args.registries[0], args.registries[1], rejected)
    else:
        targets, mapped = read_current_to_new(args.map)

    reviews = {}
    if args.review:
        review = load_json(args.review)
        for choice in review.get("current_choices", []):
            model = MODELS[choice["sex"]]
            slot = ("p%d" if choice.get("is_prop") else "c%d") % int(choice["component_id"])
            key = (model, slot, choice["collection"], int(choice["drawable"]), int(choice["texture"]))
            status = choice.get("status")
            revamp = choice.get("revamp")
            if status in ("exact", "moved") and revamp:
                reviews[key] = (runtime_collection(revamp["collection"], slot.startswith("p")), int(revamp["drawable"]), int(revamp["texture"]))
            elif status == "retired":
                reviews[key] = ("retired", choice.get("evidence") or "retired in the revamp")
            else:
                reviews[key] = ("review", choice.get("evidence") or "replacement needs a decision")

    entries = []
    uncovered = 0
    untouched = set()
    for model_key, counts in old.get("counts", {}).items():
        model = int(model_key)
        for count_key, textures in counts.items():
            col, slot = count_key.rsplit("/", 1)
            # A collection the builder's map doesn't list is unchanged only when its YMT (the file
            # that numbers its drawables and textures) is byte-identical in both revisions.
            if (model, col) not in mapped and not any(k[0] == model and k[2] == col for k in reviews):
                same_ymt = old.get("ymtSha256", {}).get(col) and old.get("ymtSha256", {}).get(col) == new.get("ymtSha256", {}).get(col)
                if same_ymt and new.get("counts", {}).get(model_key, {}).get(count_key) == textures:
                    untouched.add(col)
                    continue
            for drw, tex_count in enumerate(textures):
                for tex in range(tex_count):
                    key = (model, slot, col, drw, tex)
                    value = targets.get(key)
                    if (value is None or value == "retired") and key in reviews:
                        value = reviews[key]
                    entry = {"model": str(model), "slot": slot, "col": col, "drw": drw, "tex": tex}
                    if value is None:
                        uncovered += 1
                        entry.update({"action": "review", "why": "not in the builder's map"})
                    elif value == "retired" or (isinstance(value, tuple) and value[0] == "retired"):
                        entry.update({"action": "retired", "why": value[1] if isinstance(value, tuple) else "removed from the pack"})
                    elif isinstance(value, tuple) and value[0] == "review":
                        entry.update({"action": "review", "why": value[1]})
                    else:
                        to_col, to_drw, to_tex = value
                        if (to_col, to_drw, to_tex) == (col, drw, tex):
                            continue
                        entry.update({"action": "move", "toCol": to_col, "toDrw": to_drw, "toTex": to_tex})
                    entries.append(entry)

    source = "original-to-new (composed)" if args.originals else os.path.basename(args.map)
    step = {"from": args.__dict__["from"], "to": args.to, "source": source, "entries": entries}
    steps = [s for s in layouts["pack"]["steps"] if s["from"] != step["from"]]
    layouts["pack"]["steps"] = steps + [step]
    save_json(args.layouts, layouts)
    summary = {}
    for e in entries:
        summary[e["action"]] = summary.get(e["action"], 0) + 1
    print("step %s -> %s: %s; not covered by the map: %d; unchanged (identical YMT, not in the map): %s" % (
        step["from"], step["to"], summary, uncovered, ", ".join(sorted(untouched)) or "none"))
    for (which, department, collection), count in sorted(rejected.items()):
        print("rejected %d %s-build rows: %s claims %s, which the registry says it doesn't own" % (count, which, department, collection))


# ---- Checks -----------------------------------------------------------------------------------

def cmd_check(args):
    layouts = read_layouts(args.layouts)
    problems = []
    legacy = layouts["legacy"]
    for model, body in (legacy.get("models") or {}).items():
        if layout_print(body.get("slots") or {}) != body.get("layout"):
            problems.append("baseline %s print mismatch" % model)
    ids = [r["id"] for r in layouts["pack"]["revisions"]]
    if len(ids) != len(set(ids)):
        problems.append("duplicate revision ids")
    if legacy.get("models") and legacy.get("rev") not in ids:
        problems.append("baseline revision %s is not declared" % legacy.get("rev"))
    if layouts["pack"].get("current") and layouts["pack"]["current"] not in ids:
        problems.append("current revision is not declared")
    froms = [s["from"] for s in layouts["pack"]["steps"]]
    if len(froms) != len(set(froms)):
        problems.append("two steps leave the same revision")
    for s in layouts["pack"]["steps"]:
        if s["from"] not in ids or s["to"] not in ids:
            problems.append("step %s -> %s uses an undeclared revision" % (s["from"], s["to"]))
    if args.capture:
        capture = load_json(args.capture)
        models = validate_capture(capture)
        for name, model in (("mp_m_freemode_01", MALE), ("mp_f_freemode_01", FEMALE)):
            body = models[str(model)]
            base = (legacy.get("models") or {}).get(str(model))
            rev = match_revision(layouts, model, capture_counts(body))
            print("%s: layout %s (%s), pack revision %s" % (
                name, body["layout"], "baseline" if base and base["layout"] == body["layout"] else "not the baseline", rev or "unknown"))
    for p in problems:
        print("PROBLEM:", p)
    if problems:
        raise Fail("%d problem(s)" % len(problems))
    print("ok: baseline %s, revisions %s, current %s, steps %d" % (
        legacy.get("id") or "not captured", ids, layouts["pack"].get("current"), len(layouts["pack"]["steps"])))


def cmd_print(args):
    capture = load_json(args.capture)
    for model, body in (capture.get("models") or {}).items():
        print(model, layout_print(body.get("slots") or {}))


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="command", required=True)
    r = sub.add_parser("revision")
    r.add_argument("--layouts", required=True)
    r.add_argument("--id", required=True)
    r.add_argument("--ymt", required=True, action="append")
    r.add_argument("--notes")
    r.add_argument("--current", action="store_true")
    b = sub.add_parser("baseline")
    b.add_argument("--layouts", required=True)
    b.add_argument("--capture", required=True)
    b.add_argument("--id", required=True)
    b.add_argument("--valid-from", required=True)
    m = sub.add_parser("remap")
    m.add_argument("--layouts", required=True)
    m.add_argument("--from", required=True)
    m.add_argument("--to", required=True)
    source = m.add_mutually_exclusive_group(required=True)
    source.add_argument("--map", help="the new build's BUILD_REPORT/current-to-new.csv (department choices only)")
    source.add_argument("--originals", nargs=2, metavar=("OLD", "NEW"), help="both builds' BUILD_REPORT/original-to-new.csv (every choice; recommended)")
    m.add_argument("--registries", nargs=2, metavar=("OLD", "NEW"), help="both builds' BUILD_REPORT/Numbering/current-addon-registry.csv (with --originals)")
    m.add_argument("--review")
    c = sub.add_parser("check")
    c.add_argument("--layouts", required=True)
    c.add_argument("--capture")
    q = sub.add_parser("print")
    q.add_argument("--capture", required=True)
    args = p.parse_args(argv)
    try:
        {"revision": cmd_revision, "baseline": cmd_baseline, "remap": cmd_remap, "check": cmd_check, "print": cmd_print}[args.command](args)
    except Fail as e:
        print("ERROR:", e, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
