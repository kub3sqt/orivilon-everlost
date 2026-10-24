"""v003: zúžení zápěstí při přetočení (candy-wrapper). Pro vrcholy těla do ±2 cm od zápěstního
kloubu: průměrná vzdálenost od osy předloktí v póze / v rest. python3 check_wrist_volume.py -- file.blend Akce [sub]"""
import sys, bpy, numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0]); act = bpy.data.actions[args[1]]; sub = int(args[2]) if len(args) > 2 else 2
ob = bpy.data.objects['Traveler_Rig']; body = bpy.data.objects['Body']
for m in body.modifiers:
    if m.type == 'MASK': m.show_viewport = False
res = {}
sel = {}
for s in ('.L', '.R'):
    b = ob.data.bones['hand' + s]; W = b.head_local; fa = ob.data.bones['forearm' + s]
    ax = (fa.tail_local - fa.head_local).normalized()
    sel[s] = [v.index for v in body.data.vertices if abs((v.co - W).dot(ax)) < 0.02 and (v.co - W - ax * (v.co - W).dot(ax)).length < 0.07]
def radii(co, s, P):
    W = P['hand' + s].head; ax = (P['forearm' + s].tail - P['forearm' + s].head).normalized()
    return np.mean([((co[i] - W) - ax * (co[i] - W).dot(ax)).length for i in sel[s]])
ob.animation_data.action = None
for pb in ob.pose.bones: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
bpy.context.view_layer.update(); dg = bpy.context.evaluated_depsgraph_get()
me = body.evaluated_get(dg).to_mesh(); co = [v.co.copy() for v in me.vertices]
r0 = {s: radii(co, s, ob.pose.bones) for s in sel}
ob.animation_data.action = act; sc = bpy.context.scene
worst = {s: (9, 0) for s in sel}
for f in range(int(act.frame_start), int(act.frame_end)):
    for k in range(sub):
        sc.frame_set(f, subframe=k / sub); dg = bpy.context.evaluated_depsgraph_get()
        be = body.evaluated_get(dg); me = be.to_mesh(); co = [be.matrix_world @ v.co for v in me.vertices]
        for s in sel:
            r = radii(co, s, ob.pose.bones) / r0[s]
            if r < worst[s][0]: worst[s] = (round(r, 3), f + k / sub)
        be.to_mesh_clear()
print('WRISTVOL', args[1], worst)
