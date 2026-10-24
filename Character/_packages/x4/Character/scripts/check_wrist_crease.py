"""v004: rýha/zlom na zápěstí – nejmenší poměr plochy (póza/rest) ploch těla do 5,5 cm od zápěstního
kloubu (silné stlačení = rýha), plus nejmenší úhel mezi normálami sousedních ploch (ostrý lom).
python3 check_wrist_crease.py -- file.blend Akce [sub]"""
import sys, bpy, numpy as np, math
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0]); act = bpy.data.actions[args[1]]; sub = int(args[2]) if len(args) > 2 else 2
ob = bpy.data.objects['Traveler_Rig']; body = bpy.data.objects['Body']
for m in body.modifiers:
    if m.type == 'MASK': m.show_viewport = False
me0 = body.data
sel = {}
for s in ('.L', '.R'):
    W = ob.data.bones['hand' + s].head_local
    sel[s] = [p.index for p in me0.polygons if (p.center - W).length < 0.055]
area0 = np.array([p.area for p in me0.polygons])
# sousední plochy
edge_f = {}
for p in me0.polygons:
    for k in p.edge_keys: edge_f.setdefault(k, []).append(p.index)
pairs = {s: [(a, b) for k, (a, b) in ((k, v) for k, v in edge_f.items() if len(v) == 2) if a in set(sel[s]) and b in set(sel[s])] for s in sel}
ob.animation_data.action = act; sc = bpy.context.scene
worst = {s: [9.0, 0, 0.0, 0] for s in sel}
for f in range(int(act.frame_start), int(act.frame_end)):
    for k in range(sub):
        sc.frame_set(f, subframe=k / sub); dg = bpy.context.evaluated_depsgraph_get()
        be = body.evaluated_get(dg); me = be.to_mesh()
        ar = np.array([p.area for p in me.polygons]); nr = [p.normal.copy() for p in me.polygons]
        for s in sel:
            r = (ar[sel[s]] / area0[sel[s]]).min()
            if r < worst[s][0]: worst[s][0] = round(float(r), 3); worst[s][1] = f + k / sub
            mx = max(math.degrees(nr[a].angle(nr[b])) for a, b in pairs[s])
            if mx > worst[s][2]: worst[s][2] = round(mx, 1); worst[s][3] = f + k / sub
        be.to_mesh_clear()
print('CREASE', args[1], {s: {'min_area_ratio': w[0], '@': w[1], 'max_fold_deg': w[2], '@@': w[3]} for s, w in worst.items()})
