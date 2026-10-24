"""Rychlá kontrola jednotlivých klíčových snímků: 4 pohledy + detail rukou + metriky.
python3 pose_lab.py -- file.blend Action frames=0,14 out.png [tool=Axe] [hands]"""
import sys, bpy, os
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import render_util as RU
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0])
act = bpy.data.actions[args[1]]
frames = [float(x) for x in next(a for a in args if a.startswith('frames=')).split('=')[1].split(',')]
out = next(a for a in args if a.endswith('.png'))
tool = next((a.split('=')[1] for a in args if a.startswith('tool=')), act.get('tool', 'none'))
ob = bpy.data.objects['Traveler_Rig']; ob.animation_data.action = act; ob.hide_render = True
for t in ('Tool_Axe', 'Tool_Pickaxe'):
    o = bpy.data.objects.get(t)
    if o: o.hide_render = (t != 'Tool_' + str(tool))
for o in bpy.data.objects:
    if o.name.startswith('PROXY_'):
        o.hide_render = 'proxy' not in args
body = bpy.data.objects['Body']
mb = bpy.data.materials.new('wb'); mb.diffuse_color = (0.86, 0.84, 0.79, 1); body.data.materials.clear(); body.data.materials.append(mb)
RU.setup_workbench(res=(360, 400))
bpy.context.scene.display.shading.show_shadows = False
V = {'front': (0, -1, 0.15), 'sideR': (-1, 0, 0.1), 'side': (1, 0, 0.1), 'top': (0.001, 0, 1), 'q34r': (-0.9, -0.7, 0.45), 'back': (0, 1, 0.2)}
views = next((a.split('=')[1] for a in args if a.startswith('views=')), 'front,sideR,top,q34r').split(',')
paths = []; labels = []
sc = bpy.context.scene
for f in frames:
    sc.frame_set(int(f), subframe=f - int(f))
    for v in views:
        RU.camera(target=Vector((0, -0.15, 0.75)), d=Vector(V[v]).normalized(), ortho_scale=1.8)
        p = f'/tmp/_pl_{f}_{v}.png'; RU.render(p); paths.append(p); labels.append(f'f{f} {v}')
    if 'hands' in args:
        P = ob.pose.bones
        for s in ('.R', '.L'):
            c = ob.matrix_world @ P['hand' + s].tail
            RU.camera(target=c, d=Vector((-0.6, -1, 0.5)).normalized(), ortho_scale=0.35)
            p = f'/tmp/_pl_{f}_h{s}.png'; RU.render(p); paths.append(p); labels.append(f'f{f} hand{s}')
RU.compose(paths, out, cols=len(views) + (2 if 'hands' in args else 0), labels=labels)
