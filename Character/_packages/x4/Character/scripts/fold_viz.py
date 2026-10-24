"""v003: zobrazí přeložené plochy (červeně) na snímku. python3 fold_viz.py -- file.blend Akce frame out.png [parts=Outfit_Mantle,Outfit_Tunic] [target=chest] [scale=0.7]"""
import sys, os, bpy, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_util as RU
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0])
an, fr, out = args[1], float(args[2]), args[3]
kv = {a.split('=')[0]: a.split('=')[1] for a in args if '=' in a}
parts = kv.get('parts', 'Outfit_Mantle,Outfit_Tunic').split(',')
rig = bpy.data.objects['Traveler_Rig']
if 'Body' in parts:
    for m in bpy.data.objects['Body'].modifiers:
        if m.type == 'MASK': m.show_viewport = False; m.show_render = False
def evald(o):
    dg = bpy.context.evaluated_depsgraph_get(); oe = o.evaluated_get(dg); me = bpy.data.meshes.new_from_object(oe)
    me.transform(oe.matrix_world); return me
rig.animation_data.action = None
for pb in rig.pose.bones: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
bpy.context.view_layer.update()
rest = {p: evald(bpy.data.objects[p]) for p in parts}
rig.animation_data.action = bpy.data.actions[an]
sc = bpy.context.scene; sc.frame_set(int(fr), subframe=fr - int(fr))
Rb = {pb.name: (pb.matrix.to_3x3() @ pb.bone.matrix_local.to_3x3().inverted()) for pb in rig.pose.bones}
red = bpy.data.materials.new('RED'); red.diffuse_color = (1, 0, 0, 1)
for p in parts:
    o = bpy.data.objects[p]
    names = {g.index: g.name for g in o.vertex_groups}
    vb = []
    for v in o.data.vertices:
        gs = [g for g in v.groups if names[g.group] in rig.pose.bones]
        vb.append(names[max(gs, key=lambda g: g.weight).group] if gs else 'root')
    co = np.array([v.co[:] for v in o.data.vertices])
    me = evald(o); r = rest[p]
    me.materials.append(red); ri = len(me.materials) - 1
    for pr, p0 in zip(me.polygons, r.polygons):
        b = vb[int(np.argmin(((co - np.array(p0.center[:])) ** 2).sum(1)))]
        e = Rb.get(b, Rb['root']) @ p0.normal
        if e.dot(pr.normal) < -0.17:
            pr.material_index = ri
    ob = bpy.data.objects.new(p + '_viz', me); sc.collection.objects.link(ob)
    o.hide_render = True
for o in bpy.data.objects:
    if o.name.startswith('PROXY') or o.name.startswith('Tool_'): o.hide_render = True
for h in kv.get('hide', '').split(','):
    if h and bpy.data.objects.get(h): bpy.data.objects[h].hide_render = True
RU.setup_workbench(res=(380, 380))
tgt = rig.matrix_world @ rig.pose.bones[kv.get('target', 'chest')].head
V = {'front': (0, -1, 0.2), 'back': (0, 1, 0.2), 'sideR': (-1, 0, 0.15), 'side': (1, 0, 0.15), 'upR': (-0.6, 0.3, 0.9), 'q34rb': (-0.8, 0.7, 0.4), 'q34lb': (0.8, 0.7, 0.4), 'under': (0, -0.3, -1)}
paths = []
for v in kv.get('views', 'front,back,sideR,side,q34rb,q34lb').split(','):
    RU.camera(target=tgt, d=Vector(V[v]).normalized(), ortho_scale=float(kv.get('scale', 0.7)), dist=3)
    pth = f'/tmp/_fv_{os.path.basename(out)}_{v}.png'; RU.render(pth); paths.append(pth)
RU.compose(paths, out, cols=6, labels=[f'{an} f{fr} {v}' for v in kv.get('views', 'front,back,sideR,side,q34rb,q34lb').split(',')])
