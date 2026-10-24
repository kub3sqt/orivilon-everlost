"""v003: kontrola orientace normál (rubové plochy). Pro každý mesh (vyhodnocený vč. Solidify) se
z každé plochy vystřelí paprsek po normále; lichý počet zásahů vlastní geometrie = normála míří dovnitř.
python3 check_normals.py -- file.blend [rest]"""
import sys, bpy
from mathutils.bvhtree import BVHTree
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0])
rig = bpy.data.objects.get('Traveler_Rig')
if rig and rig.animation_data: rig.animation_data.action = None
if rig:
    for pb in rig.pose.bones: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
res = {}
for ob in bpy.data.objects:
    if ob.type != 'MESH' or ob.name.startswith('PROXY') or ob.name in ('Body',): continue
    oe = ob.evaluated_get(dg); me = oe.to_mesh()
    vs = [oe.matrix_world @ v.co for v in me.vertices]; ps = [tuple(p.vertices) for p in me.polygons]
    bvh = BVHTree.FromPolygons(vs, ps)
    R = oe.matrix_world.to_3x3()
    bad = 0; tot = 0; edges_open = sum(1 for e in me.edges) 
    for p in me.polygons:
        c = oe.matrix_world @ p.center; n = (R @ p.normal).normalized()
        o = c + n * 1e-4; hits = 0
        for _ in range(30):
            h = bvh.ray_cast(o, n)
            if h[0] is None: break
            hits += 1; o = h[0] + n * 1e-4
        tot += 1; bad += hits % 2
    res[ob.name] = round(bad / max(tot, 1), 3)
    oe.to_mesh_clear()
for k, v in sorted(res.items(), key=lambda x: -x[1]):
    print(f'NORM {k:28s} inward_frac {v}')
