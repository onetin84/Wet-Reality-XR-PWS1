# Statische Messung: welche Skripte benutzen die serialisierten MonoBehaviours
# im Player-Build selbst (data.unity3d: Szenen, globalgamemanagers,
# sharedassets, resources)? Ergaenzt scan_bundle_scripts.py, das nur die
# Addressables-Bundles liest. Gleicher MonoBehaviour-Kopf (Unity 2020.3):
# m_Script PPtr ab Byte 16. Zusaetzlich: je Treffer der GameObject-Name,
# fuer Klassen, die auf das Muster in argv[3] passen. Schreibt nur die
# Ausgabedatei.
import sys, os, struct, json, collections, re, UnityPy

src = sys.argv[1]
out_path = sys.argv[2]
pattern = re.compile(sys.argv[3] if len(sys.argv) > 3 else r"Radial|^PWS\.XR")

env = UnityPy.load(src)
scripts = {}                               # (file_lower, path_id) -> (ns, class, asm)
uses = collections.defaultdict(list)       # (file_lower, path_id) -> [(file, go_pid)]
gos = {}                                   # (file_lower, go_pid) -> name
errors = []
files_seen = set()

for obj in env.objects:
    af = obj.assets_file
    files_seen.add(af.name)
    t = obj.type.name
    try:
        if t == "MonoScript":
            d = obj.read(check_read=False)
            scripts[(af.name.lower(), obj.path_id)] = (d.m_Namespace, d.m_ClassName, d.m_AssemblyName)
        elif t == "MonoBehaviour":
            raw = obj.get_raw_data()
            go_pid = struct.unpack_from("<q", raw, 4)[0]
            fid, pid = struct.unpack_from("<iq", raw, 16)
            if fid == 0:
                f = af.name.lower()
            else:
                f = os.path.basename(af.externals[fid - 1].path).lower()
            uses[(f, pid)].append((af.name, go_pid))
        elif t == "GameObject":
            d = obj.read(check_read=False)
            gos[(af.name.lower(), obj.path_id)] = d.m_Name
    except Exception as e:
        errors.append((af.name, t, repr(e)))

result = {
    "files": sorted(files_seen),
    "scripts_found": len(scripts),
    "error_count": len(errors), "errors": errors[:100],
    "all_script_classes": sorted(".".join(x for x in s[:2] if x) for s in scripts.values()),
    "used": [], "matches": [],
}
for key, lst in uses.items():
    cls = scripts.get(key)
    name = ".".join(x for x in cls[:2] if x) if cls else None
    result["used"].append({"file": key[0], "path_id": key[1], "class": name, "count": len(lst),
                           "in": sorted(set(x[0] for x in lst))[:20]})
    if name and pattern.search(name):
        result["matches"].append({"class": name, "count": len(lst),
                                  "objects": [(f, gos.get((f.lower(), g), "?")) for f, g in lst][:30]})
result["used"].sort(key=lambda u: -u["count"])

with open(out_path, "w", encoding="utf-8") as fh:
    json.dump(result, fh, indent=1, ensure_ascii=False)
print(f"fertig: {len(files_seen)} Dateien, {len(scripts)} MonoScripts, "
      f"{len(uses)} benutzte Skriptzeiger, {len(result['matches'])} Muster-Treffer, {len(errors)} Fehler")
for m in result["matches"]:
    print("  ", m["class"], m["count"], m["objects"][:5])
