"""v003: hledání přeložených / převrácených ploch oblečení v pohybu.
Pro každý díl a každý (pod)snímek: plochy, jejichž normála se vůči rest póze (po odečtení rotace
nejvlivnější kosti) otočí o > 100° = přeložená látka (rub navenek). Počítá se i Body.
python3 check_folds.py -- file.blend Akce1,Akce2 [sub=2] [out.json]"""
import sys, json, bpy, numpy as np
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0])
acts = args[1].split(',')
sub = int(next((a.split('=')[1] for a in args if a.startswith('sub=')), 2))
outj = next((a for a in args if a.endswith('.json')), None)
rig = bpy.data.objects['Traveler_Rig']
objs = [o for o in bpy.data.objects if o.type == 'MESH' and (o.name.startswith('Outfit_') or o.name == 'Body')]
for m in bpy.data.objects['Body'].modifiers:
    if m.type == 'MASK': m.show_viewport = False
sc = bpy.context.scene

def mesh_data(o, dg):
    oe = o.evaluated_get(dg); me = oe.to_mesh()
    n = len(me.polygons)
    nr = np.zeros(n * 3); me.polygons.foreach_get('normal', nr)
    ce = np.zeros(n * 3); me.polygons.foreach_get('center', ce)
    oe.to_mesh_clear()
    return nr.reshape(-1, 3), ce.reshape(-1, 3)

# rest
rig.animation_data.action = None
for pb in rig.pose.bones: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
bpy.context.view_layer.update(); dg = bpy.context.evaluated_depsgraph_get()
rest = {o.name: mesh_data(o, dg) for o in objs}
# dominantní kost každé plochy (podle nejbližšího vrcholu) – kompenzace rotace kosti
dom = {}
for o in objs:
    names = {g.index: g.name for g in o.vertex_groups}
    vb = []
    for v in o.data.vertices:
        gs = [g for g in v.groups if names[g.group] in rig.pose.bones]
        vb.append(names[max(gs, key=lambda g: g.weight).group] if gs else 'root')
    # evaluovaný mesh může mít víc ploch (solidify) -> přiřadit podle nejbližšího vrcholu
    co = np.array([v.co[:] for v in o.data.vertices])
    ce = rest[o.name][1]
    idx = [int(np.argmin(((co - c) ** 2).sum(1))) for c in ce]
    dom[o.name] = [vb[i] for i in idx]
rep = {}
for an in acts:
    act = bpy.data.actions[an]; rig.animation_data.action = act
    worst = {}
    for f in range(int(act.frame_start), int(act.frame_end)):
        for k in range(sub):
            sc.frame_set(f, subframe=k / sub); dg = bpy.context.evaluated_depsgraph_get()
            Rb = {pb.name: (pb.matrix.to_3x3() @ pb.bone.matrix_local.to_3x3().inverted()) for pb in rig.pose.bones}
            for o in objs:
                nr, ce = mesh_data(o, dg)
                n0 = rest[o.name][0]
                if len(nr) != len(n0): continue
                R = np.array([np.array(Rb.get(b, Rb['root'])) for b in dom[o.name]])
                exp = np.einsum('nij,nj->ni', R, n0)
                d = (exp * nr).sum(1)
                bad = int((d < -0.17).sum())   # > ~100°
                if bad > worst.get(o.name, (0, 0))[0]:
                    from collections import Counter
                    where = Counter(dom[o.name][i] for i in np.nonzero(d < -0.17)[0]).most_common(3)
                    worst[o.name] = (bad, round(f + k / sub, 2), where)
    rep[an] = worst
    print('FOLDS', an, {k: v for k, v in sorted(worst.items(), key=lambda x: -x[1][0]) if v[0] > 0})
if outj: json.dump(rep, open(outj, 'w'), indent=1)
