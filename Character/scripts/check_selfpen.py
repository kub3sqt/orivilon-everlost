"""v002: průnik paží do skutečné geometrie těla (trup, stehna, hlava) – posed mesh, ne elipsoidy.
python3 check_selfpen.py -- file.blend Akce [sub]"""
import sys, bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0]); act = bpy.data.actions[args[1]]; sub = int(args[2]) if len(args) > 2 else 2
ob = bpy.data.objects['Traveler_Rig']; ob.animation_data.action = act
body = bpy.data.objects['Body']
for m in body.modifiers:
    if m.type == 'MASK': m.show_viewport = False
names = {g.index: g.name for g in body.vertex_groups}
dom = [names[max(v.groups, key=lambda x: x.weight).group] if v.groups else '' for v in body.data.vertices]
ARMB = ('forearm', 'hand', 'fingers', 'thumb')
TOR = ('hips', 'spine', 'chest', 'neck', 'head', 'thigh', 'shin', 'foot', 'toe', 'shoulder')
arm = {s: [i for i, d in enumerate(dom) if d.split('.')[0] in ARMB and d.endswith(s)] for s in ('.L', '.R')}
tpolys = [tuple(p.vertices) for p in body.data.polygons if all(dom[v].split('.')[0] in TOR for v in p.vertices)]
sc = bpy.context.scene; worst = {}; WH = None
for f in range(int(act.frame_start), int(act.frame_end)):
    for k in range(sub):
        sc.frame_set(f, subframe=k / sub)
        dg = bpy.context.evaluated_depsgraph_get(); be = body.evaluated_get(dg); me = be.to_mesh()
        co = [be.matrix_world @ v.co for v in me.vertices]; be.to_mesh_clear()
        bvh = BVHTree.FromPolygons(co, tpolys)
        for s, idx in arm.items():
            d = 0.0
            for i in idx:
                h = bvh.find_nearest(co[i], 0.06)
                if h[0] is not None and (co[i] - h[0]).dot(h[1]) < 0:
                    if h[3] > d: d = h[3]; WH = (dom[i], dom[tpolys[h[2]][0]], h[1].dot(Vector((0,0,1))))
            if d > worst.get(s, (0, 0))[0]:
                worst[s] = (d, f + k / sub, WH)
print('SELFPEN', args[1], {s: (round(v[0] * 1000, 1), v[1], v[2]) for s, v in worst.items()})
