"""Run: python tests/clothing-layouts/test_clothing_layouts.py

Drives tools/clothing-layouts/clothing_layouts.py: pack revisions from YMT exports, a capture
becoming the baseline only when it is continuous and matches a declared pack, remaps that account
for every old drawable/texture (anything the builder's map doesn't cover becomes "review"), and the
prints shared with vMenu/ClothingRefs.cs.
"""
import contextlib
import io
import json
import os
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools", "clothing-layouts"))
import clothing_layouts as tool  # noqa: E402

MALE, FEMALE = str(tool.MALE), str(tool.FEMALE)
checks = 0


def check(condition, message):
    global checks
    checks += 1
    if not condition:
        raise SystemExit("FAIL: " + message)


def run(*args):
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = tool.main(list(args))
    return code, out.getvalue() + err.getvalue()


def ymt(name, components, props=()):
    """components: {comp_id: [texture counts]}, props: [(anchor, prop_id, textures)]"""
    avail, data = [], []
    for comp in range(12):
        if comp in components:
            avail.append(str(len(data)))
            drawables = "".join(
                "<Item><propMask value=\"1\" /><numAlternatives value=\"0\" /><aTexData itemType=\"CPVTextureData\">%s</aTexData></Item>"
                % "".join("<Item><texId value=\"0\" /><distribution value=\"255\" /></Item>" for _ in range(t)) for t in components[comp])
            data.append("<Item><numAvailTex value=\"1\" /><aDrawblData3 itemType=\"CPVDrawblData\">%s</aDrawblData3></Item>" % drawables)
        else:
            avail.append("255")
    prop_items = "".join(
        "<Item><texData itemType=\"CPedPropTexData\">%s</texData><anchorId value=\"%d\" /><propId value=\"%d\" /></Item>"
        % ("".join("<Item><weight value=\"1\" /></Item>" for _ in range(t)), anchor, pid) for anchor, pid, t in props)
    return ("<?xml version='1.0' encoding='utf-8'?><CPedVariationInfo name=\"%s\"><availComp>%s</availComp>"
            "<aComponentData3 itemType=\"CPVComponentData\">%s</aComponentData3><propInfo><numAvailProps value=\"%d\" />"
            "<aPropMetaData itemType=\"CPedPropMetaData\">%s</aPropMetaData></propInfo></CPedVariationInfo>"
            % (name, " ".join(avail), "".join(data), len(props), prop_items))


def capture(male_dept_c11, female_ok=True, anomalies=(), gap=False):
    def body(rows_by_slot):
        slots = {k: v for k, v in rows_by_slot.items()}
        return {"name": "x", "layout": tool.layout_print(slots), "anomalies": list(anomalies), "slots": slots, "collections": [""]}
    male_rows = {
        "c11": [["", 0, 3, [2, 2, 2]], ["mp_m_deptmerge2", 3 + (1 if gap else 0), len(male_dept_c11), male_dept_c11]],
        "c4": [["", 0, 2, [1, 1]], ["mp_m_deptmerge2", 2, 2, [1, 2]]],
        "p0": [["", 0, 1, [3]], ["mp_m_deptmerge2", 1, 2, [1, 1]]],
        "c9": [["", 0, 2, [1, 1]], ["mp_m_leoaccessories", 2, 2, [1, 1]]],
    }
    female_rows = {"c11": [["", 0, 2, [1, 1]], ["mp_f_deptmerge2", 2, 1, [4]]]}
    if not female_ok:
        female_rows["c11"][1][3] = [5]
    male = body(male_rows)
    if gap:
        male["layout"] = "0" * 16
    return {"kind": "vmenu-clothing-capture", "version": 1, "gameBuild": 3258, "capturedAt": "2026-11-10T12:00:00Z",
            "models": {MALE: male, FEMALE: body(female_rows)}}


def write(path, text):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return path


def main():
    # Prints pinned to the same values the C# test checks (tests/clothing-refs).
    check(tool.fnv64("") == "cbf29ce484222325" and tool.fnv64("vMenu") == "2f255d694ef3b77e", "FNV-1a 64")
    check(tool.layout_print({"c11": [["", 0, 3, [1, 1, 1]], ["mp_m_deptmerge2", 3, 2, [1, 3]]], "p0": [["", 0, 1, [2]]]}) == "29c1b6590c7eb66e", "layout print")
    check(tool.pack_print(["mp_m_deptmerge2"], lambda c, k: [1, 3] if (c, k) == ("mp_m_deptmerge2", "c11") else None) == "04b248cc583fef0f", "pack print")

    with tempfile.TemporaryDirectory() as tmp:
        layouts = os.path.join(tmp, "clothing-layouts.json")
        m_a = write(os.path.join(tmp, "mp_m_freemode_01_mp_m_deptmerge2.xml"), ymt("mp_m_deptmerge2", {11: [1, 3, 2], 4: [1, 2]}, [(0, 0, 1), (0, 1, 1)]))
        f_a = write(os.path.join(tmp, "mp_f_freemode_01_mp_f_deptmerge2.xml"), ymt("mp_f_deptmerge2", {11: [4]}))
        leo = write(os.path.join(tmp, "mp_m_freemode_01_mp_m_leoaccessories.xml"), ymt("mp_m_leoaccessories", {9: [1, 1]}))
        code, out = run("revision", "--layouts", layouts, "--id", "A", "--ymt", m_a, "--ymt", f_a, "--ymt", leo)
        check(code == 0, "revision A added: " + out)
        data = json.load(open(layouts))
        rev = data["pack"]["revisions"][0]
        check(data["pack"]["current"] == "A" and rev["collections"] == ["mp_f_deptmerge2", "mp_m_deptmerge2", "mp_m_leoaccessories"], "collections and current revision")
        check(len(rev["ymtSha256"]) == 3, "each collection's YMT is fingerprinted")
        check(rev["counts"][MALE] == {"mp_m_deptmerge2/c4": [1, 2], "mp_m_deptmerge2/c11": [1, 3, 2], "mp_m_deptmerge2/p0": [1, 1], "mp_m_leoaccessories/c9": [1, 1]}, "male texture counts from the YMT")
        check(data["legacy"]["rev"] is None and data["legacy"]["models"] == {}, "no legacy pack is claimed before a capture")

        # Baseline: only a continuous capture whose pack matches a declared revision.
        cap = write(os.path.join(tmp, "cap-wrong.json"), json.dumps(capture([1, 3, 2, 1])))
        code, out = run("baseline", "--layouts", layouts, "--capture", cap, "--id", "live", "--valid-from", "2026-11-06")
        check(code == 1 and "don't exactly match" in out, "a capture of a different pack is refused")
        for name, bad in (("anomalies", capture([1, 3, 2], anomalies=["c11 global 3: local 2"])), ("gap", capture([1, 3, 2], gap=True)), ("female", capture([1, 3, 2], female_ok=False))):
            code, out = run("baseline", "--layouts", layouts, "--capture", write(os.path.join(tmp, name + ".json"), json.dumps(bad)), "--id", "live", "--valid-from", "2026-11-06")
            check(code == 1, "a bad capture is refused: " + name)
        good = write(os.path.join(tmp, "cap.json"), json.dumps(capture([1, 3, 2])))
        code, out = run("baseline", "--layouts", layouts, "--capture", good, "--id", "live", "--valid-from", "2026-11-06T00:00:00Z")
        check(code == 0, "a matching capture becomes the baseline: " + out)
        data = json.load(open(layouts))
        check(data["legacy"]["rev"] == "A" and data["legacy"]["validFromUnix"] == 1793923200 and set(data["legacy"]["models"]) == {MALE, FEMALE}, "baseline: pack A, valid from, both models")
        code, out = run("check", "--layouts", layouts, "--capture", good)
        check(code == 0 and "(baseline)" in out and "pack revision A" in out, "check: the capture's server matches: " + out)

        # Revision B: FIB retires drawable 2 of the jacket and reorders drawable 1's textures; lower body untouched.
        os.makedirs(os.path.join(tmp, "b"))
        m_b = write(os.path.join(tmp, "b", "mp_m_freemode_01_mp_m_deptmerge2.xml"), ymt("mp_m_deptmerge2", {11: [1, 3], 4: [1, 2]}, [(0, 0, 1), (0, 1, 1)]))
        code, out = run("revision", "--layouts", layouts, "--id", "B", "--ymt", m_b, "--ymt", f_a, "--ymt", leo, "--current")
        check(code == 0 and json.load(open(layouts))["pack"]["current"] == "B", "revision B is current")
        header = "department,sex,collection,component,old_drawable,old_texture,status,new_drawable,new_texture\n"
        rows = [
            "LSPD,m,mp_m_deptmerge2,jbib,0,0,retained,0,0",
            "LSPD,m,mp_m_deptmerge2,jbib,1,0,retained,1,1",
            "LSPD,m,mp_m_deptmerge2,jbib,1,1,retained,1,2",
            "LSPD,m,mp_m_deptmerge2,jbib,1,2,retained,1,0",
            "FIB,m,mp_m_deptmerge2,jbib,2,0,removed,,",
            "FIB,m,mp_m_deptmerge2,jbib,2,1,removed,,",
            "LSPD,m,mp_m_deptmerge2,lowr,0,0,retained,0,0",
            "LSPD,m,mp_m_deptmerge2,lowr,1,0,retained,1,0",
            "LSPD,m,mp_m_deptmerge2,lowr,1,1,retained,1,1",
            "LSPD,m,mp_m_deptmerge2,p_head,0,0,retained,0,0",
            "LSPD,f,mp_f_deptmerge2,jbib,0,0,retained,0,0",
            "LSPD,f,mp_f_deptmerge2,jbib,0,1,retained,0,1",
            "LSPD,f,mp_f_deptmerge2,jbib,0,2,retained,0,2",
            "LSPD,f,mp_f_deptmerge2,jbib,0,3,retained,0,3",
        ]
        csv_path = write(os.path.join(tmp, "current-to-new.csv"), header + "\n".join(rows) + "\n")
        code, out = run("remap", "--layouts", layouts, "--from", "A", "--to", "B", "--map", csv_path)
        check(code == 0 and "not covered by the map: 1" in out and "unchanged (identical YMT, not in the map): mp_m_leoaccessories" in out,
              "remap built; the one uncovered prop is counted; the untouched collection is proven unchanged: " + out)
        step = json.load(open(layouts))["pack"]["steps"][0]
        by = {(e["slot"], e["drw"], e["tex"]): e for e in step["entries"]}
        check(by[("c11", 1, 0)]["action"] == "move" and by[("c11", 1, 0)]["toTex"] == 1 and by[("c11", 1, 2)]["toTex"] == 0, "texture reorder entries")
        check(by[("c11", 2, 0)]["action"] == "retired" and by[("c11", 2, 1)]["action"] == "retired", "retired drawable")
        check(by[("p0", 1, 0)]["action"] == "review" and "not in the builder's map" in by[("p0", 1, 0)]["why"], "an item the map doesn't cover is never assumed unchanged")
        check(("c11", 0, 0) not in by and ("c4", 1, 1) not in by, "unchanged items need no entry")

        # The FIB review: a retired FIB jacket texture kept exactly in the revamp's own collection,
        # and a replacement that needs a decision.
        review = {"current_choices": [
            {"sex": "m", "component_id": 11, "is_prop": False, "collection": "mp_m_deptmerge2", "drawable": 2, "texture": 0, "status": "exact",
             "revamp": {"collection": "mp_m_freemode_01_fibeup", "drawable": 5, "texture": 1}, "evidence": "same YDD and YTD bytes"},
            {"sex": "m", "component_id": 11, "is_prop": False, "collection": "mp_m_deptmerge2", "drawable": 2, "texture": 1, "status": "needs-decision",
             "revamp": None, "evidence": "same file name, different texture"},
            {"sex": "m", "component_id": 11, "is_prop": False, "collection": "mp_m_deptmerge2", "drawable": 1, "texture": 0, "status": "retired",
             "revamp": None, "evidence": "not in the revamp"},
        ]}
        code, out = run("remap", "--layouts", layouts, "--from", "A", "--to", "B", "--map", csv_path, "--review", write(os.path.join(tmp, "review.json"), json.dumps(review)))
        check(code == 0, "remap with review: " + out)
        by = {(e["slot"], e["drw"], e["tex"]): e for e in json.load(open(layouts))["pack"]["steps"][0]["entries"]}
        check(by[("c11", 2, 0)] == {"model": MALE, "slot": "c11", "col": "mp_m_deptmerge2", "drw": 2, "tex": 0, "action": "move", "toCol": "fibeup", "toDrw": 5, "toTex": 1},
              "an exact match in the revamp follows the garment to its new collection (named by its dlcName, as FiveM does)")
        check(by[("c11", 2, 1)]["action"] == "review" and "different texture" in by[("c11", 2, 1)]["why"], "a replacement needs a decision")
        check(by[("c11", 1, 0)]["action"] == "move" and by[("c11", 1, 0)]["toTex"] == 1, "the builder still keeps a texture another department uses: it isn't retired")
        check(len(json.load(open(layouts))["pack"]["steps"]) == 1, "re-running a remap replaces the step")

        # The same collection with a rebuilt YMT and no map: every item needs review.
        os.makedirs(os.path.join(tmp, "c"))
        leo_c = write(os.path.join(tmp, "c", "mp_m_freemode_01_mp_m_leoaccessories.xml"), ymt("mp_m_leoaccessories", {9: [1, 1]}, [(1, 0, 1)]))
        run("revision", "--layouts", layouts, "--id", "C", "--ymt", m_b, "--ymt", f_a, "--ymt", leo_c)
        code, out = run("remap", "--layouts", layouts, "--from", "A", "--to", "C", "--map", csv_path)
        step_c = [s for s in json.load(open(layouts))["pack"]["steps"] if s["to"] == "C"][0]
        leo_entries = [e for e in step_c["entries"] if e["col"] == "mp_m_leoaccessories"]
        check(code == 0 and len(leo_entries) == 2 and all(e["action"] == "review" for e in leo_entries), "a changed YMT with no map is never assumed unchanged")
        data = json.load(open(layouts))
        data["pack"]["steps"] = [s for s in data["pack"]["steps"] if s["to"] != "C"]
        data["pack"]["revisions"] = [r for r in data["pack"]["revisions"] if r["id"] != "C"]
        tool.save_json(layouts, data)
        check(tool.runtime_collection("mp_f_freemode_01_bm_fib") == "bm_fib" and tool.runtime_collection("mp_m_deptmerge2") == "mp_m_deptmerge2"
              and tool.runtime_collection("mp_f_freemode_01_p_bm_fib", prop=True) == "bm_fib" and tool.runtime_collection("p_x", prop=True) == "p_x", "collection names: dlcName")

        # Recommended source: both builds' original-to-new.csv, joined on the original choice and
        # checked against each build's registry.
        otn_header = "source_pack,sex,old_collection,component,old_drawable,old_texture,new_collection,new_drawable,new_texture,old_ytd,department,status\n"
        old_otn = write(os.path.join(tmp, "old-otn.csv"), otn_header + "\n".join([
            "department_original,m,orig_lspd,jbib,5,0,mp_m_deptmerge2,1,0,a.ytd,LSPD,retained",
            "department_original,m,orig_lspd,jbib,5,1,mp_m_deptmerge2,1,1,b.ytd,LSPD,retained",
            "department_original,m,orig_lspd,jbib,5,2,mp_m_deptmerge2,1,2,c.ytd,LSPD,retained",
            "FIB,m,bm_fib,jbib,7,0,mp_m_deptmerge2,2,0,d.ytd,FIB,retained",
            "FIB,m,bm_fib,jbib,7,1,mp_m_deptmerge2,2,1,e.ytd,FIB,retained",
            "STAFF_EUP,m,heist,jbib,9,0,mp_m_deptmerge2,0,0,f.ytd,STAFF_EUP,retained",
            "department_original,m,orig_lspd,jbib,4,0,mp_m_deptmerge2,0,0,g.ytd,LSPD,retained",
        ]) + "\n")
        new_otn = write(os.path.join(tmp, "new-otn.csv"), otn_header + "\n".join([
            "department_original,m,orig_lspd,jbib,5,0,mp_m_deptmerge2,1,1,a.ytd,LSPD,retained",
            "department_original,m,orig_lspd,jbib,5,1,mp_m_deptmerge2,1,2,b.ytd,LSPD,retained",
            "department_original,m,orig_lspd,jbib,5,2,mp_m_deptmerge2,1,0,c.ytd,LSPD,retained",
            "FIB,m,bm_fib,jbib,7,0,,,,d.ytd,FIB,removed",
            "FIB,m,bm_fib,jbib,7,1,,,,e.ytd,FIB,removed",
            "STAFF_EUP,m,heist,jbib,9,0,mp_m_deptmerge2,0,0,f.ytd,STAFF_EUP,retained",
            "department_original,m,orig_lspd,jbib,4,0,mp_m_deptmerge2,0,0,g.ytd,LSPD,retained",
        ]) + "\n")
        reg_header = "source_model,collection,component,current_drawable,texture_count,owners\n"
        old_reg = write(os.path.join(tmp, "old-reg.csv"), reg_header + "m:jbib:0,mp_m_deptmerge2,jbib,0,1,LSPD\nm:jbib:1,mp_m_deptmerge2,jbib,1,3,LSPD\nm:jbib:2,mp_m_deptmerge2,jbib,2,2,FIB\n")
        new_reg = write(os.path.join(tmp, "new-reg.csv"), reg_header + "m:jbib:0,mp_m_deptmerge2,jbib,0,1,LSPD\nm:jbib:1,mp_m_deptmerge2,jbib,1,3,LSPD\n")
        code, out = run("remap", "--layouts", layouts, "--from", "A", "--to", "B", "--originals", old_otn, new_otn)
        check(code == 1 and "--registries" in out, "originals without registries are refused")
        code, out = run("remap", "--layouts", layouts, "--from", "A", "--to", "B", "--originals", old_otn, new_otn, "--registries", old_reg, new_reg)
        check(code == 0 and "rejected 1 old-build rows: STAFF_EUP claims mp_m_deptmerge2" in out, "a row whose department doesn't own the claimed model is rejected and reported: " + out)
        by = {(e["slot"], e["drw"], e["tex"]): e for e in json.load(open(layouts))["pack"]["steps"][0]["entries"]}
        check(by[("c11", 1, 0)]["toTex"] == 1 and by[("c11", 1, 2)]["toTex"] == 0 and by[("c11", 2, 0)]["action"] == "retired", "composed originals: reorder and retirement")
        check(("c11", 0, 0) not in by and json.load(open(layouts))["pack"]["steps"][0]["source"] == "original-to-new (composed)", "the LSPD jacket #0 is unchanged; the step records its source")

        # A map that sends one item to two places is refused.
        bad_csv = write(os.path.join(tmp, "bad.csv"), header + "\n".join(rows + ["SAST,m,mp_m_deptmerge2,jbib,0,0,retained,1,1"]) + "\n")
        code, out = run("remap", "--layouts", layouts, "--from", "A", "--to", "B", "--map", bad_csv)
        check(code == 1 and "maps to both" in out, "conflicting maps are refused")
        code, out = run("check", "--layouts", layouts)
        check(code == 0 and "baseline live" in out, "the finished file checks out: " + out)

    # The shipped file.
    shipped = os.path.join(ROOT, "build", "vMenu", "config", "clothing-layouts.json")
    code, out = run("check", "--layouts", shipped)
    check(code == 0, "shipped clothing-layouts.json checks out: " + out)
    data = json.load(open(shipped))
    collections = set(data["pack"]["revisions"][-1]["collections"])
    check({"mp_m_deptmerge2", "mp_f_deptmerge2", "mp_m_leoaccessories", "mp_f_leoaccessories"} <= collections, "the four PSRP collections are declared")
    print("PASS: clothing layout tool (%d checks)" % checks)


if __name__ == "__main__":
    main()
