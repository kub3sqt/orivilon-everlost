"""v002: nejmenší vzdálenost ruky/předloktí (skutečná posed geometrie těla) od skutečných doplňků
na opasku (brašna, přezka, opasek) po celý cyklus. python3 check_bag.py -- dressed.blend Akce [sub]"""
import sys, bpy, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0]); act = bpy.data.actions[args[1]]; sub = int(args[2]) if len(args) > 2 else 2
ob = bpy.data.objects['Traveler_Rig']; ob.animation_data.action = act
body = bpy.data.objects['Body']
for m in body.modifiers:
    if m.type == 'MASK': m.show_viewport = False
names = {g.index: g.name for g in body.vertex_groups}
arm_idx = {s: [v.index for v in body.data.vertices if v.groups and names[max(v.groups, key=lambda x: x.weight).group] in
               (f'forearm.{s}', f'hand.{s}', f'fingers.{s}', f'thumb.{s}')] for s in ('L', 'R')}
acc = [bpy.data.objects[n] for n in ('Outfit_Bag', 'Outfit_BeltBuckle', 'Outfit_Belt') if bpy.data.objects.get(n)]
sc = bpy.context.scene; res = {}
worst = (1.0, None)
for f in range(int(act.frame_start), int(act.frame_end)):
    for k in range(sub):
        sc.frame_set(f, subframe=k / sub)
        dg = bpy.context.evaluated_depsgraph_get()
        be = body.evaluated_get(dg); me = be.to_mesh(); co = [be.matrix_world @ v.co for v in me.vertices]; be.to_mesh_clear()
        for a in acc:
            ae = a.evaluated_get(dg); am = ae.to_mesh()
            bvh = BVHTree.FromPolygons([ae.matrix_world @ v.co for v in am.vertices], [tuple(p.vertices) for p in am.polygons]); ae.to_mesh_clear()
            for s in ('L', 'R'):
                for i in arm_idx[s][::2]:
                    loc, nrm, idx, d = bvh.find_nearest(co[i])
                    if loc is None: continue
                    sd = (d if (co[i] - loc).dot(nrm) > 0 else -d) if a.name == 'Outfit_Bag' else d
                    key = f'{a.name}_{s}'
                    if sd < res.get(key, (9, 0))[0]:
                        res[key] = (sd, f + k / sub)
print('BAG', args[1], {k: (round(v[0] * 1000, 1), v[1]) for k, v in sorted(res.items())})
