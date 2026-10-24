"""v003 diagnostika: detailní záběry oblečení v pohybu; rubové plochy (backface) svítí purpurově.
python3 diag_closeup.py -- file.blend Action frames=1,2.5 region=shoulderR,wristR views=a,b out.png [scale=0.45] [white]"""
import sys, os, bpy
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_util as RU
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0])
act = bpy.data.actions[args[1]]
kv = {a.split('=')[0]: a.split('=')[1] for a in args if '=' in a}
frames = [float(x) for x in kv['frames'].split(',')]
regions = kv.get('region', 'shoulderR').split(',')
views = kv.get('views', 'front,back,sideR,top').split(',')
scale = float(kv.get('scale', 0.45))
out = next(a for a in args if a.endswith('.png'))
ob = bpy.data.objects['Traveler_Rig']; ob.animation_data.action = act; ob.hide_render = True
tool = act.get('tool', 'none')
for t in ('Tool_Axe', 'Tool_Pickaxe'):
    o = bpy.data.objects.get(t)
    if o: o.hide_render = (t != 'Tool_' + str(tool))
for o in bpy.data.objects:
    if o.name.startswith('PROXY_'): o.hide_render = True
if 'white' in args:
    for o in bpy.data.objects:
        if o.name.startswith('Outfit_'): o.hide_render = True
    b = bpy.data.objects['Body']
    for m in b.modifiers:
        if m.type == 'MASK': m.show_render = False
for h in kv.get('hide', '').split(','):
    if h and bpy.data.objects.get(h): bpy.data.objects[h].hide_render = True
sc = bpy.context.scene
RU.setup_workbench(res=(360, 360)); sc.display.shading.show_cavity = False
from PIL import Image, ImageChops
import numpy as np
MAGPX = []
def render_bf(p):
    sh = sc.display.shading
    sh.show_backface_culling = False; RU.render(p)
    sh.show_backface_culling = True; RU.render(p + '_c.png')
    A = Image.open(p).convert('RGB'); B = Image.open(p + '_c.png').convert('RGB')
    d = ImageChops.difference(A, B).convert('L').point(lambda x: 255 if x > 18 else 0)
    mag = Image.new('RGB', A.size, (255, 0, 255))
    Image.composite(mag, A, d).save(p)
    MAGPX.append(int(np.array(d).astype(bool).sum()))
V = {'front': (0, -1, 0.2), 'back': (0, 1, 0.2), 'sideR': (-1, 0, 0.15), 'side': (1, 0, 0.15), 'top': (0.05, -0.1, 1), 'q34r': (-0.8, -0.7, 0.5),
     'q34rb': (-0.8, 0.7, 0.4), 'q34l': (0.8, -0.7, 0.5), 'q34lb': (0.8, 0.7, 0.4), 'under': (-0.3, -0.4, -0.8), 'upR': (-0.6, -0.2, 0.9), 'upL': (0.6, -0.2, 0.9)}
P = ob.pose.bones
REG = {'shoulderR': lambda: P['upper_arm.R'].head, 'shoulderL': lambda: P['upper_arm.L'].head,
       'armpitR': lambda: P['upper_arm.R'].head.lerp(P['chest'].head, 0.35) + Vector((0, 0, -0.06)), 'armpitL': lambda: P['upper_arm.L'].head.lerp(P['chest'].head, 0.35) + Vector((0, 0, -0.06)),
       'wristR': lambda: P['hand.R'].head, 'wristL': lambda: P['hand.L'].head, 'kneeR': lambda: P['shin.R'].head, 'kneeL': lambda: P['shin.L'].head,
       'hipR': lambda: P['thigh.R'].head, 'hipL': lambda: P['thigh.L'].head, 'torso': lambda: P['chest'].head, 'legs': lambda: P['thigh.R'].head.lerp(P['thigh.L'].head, 0.5) + Vector((0, 0, -0.2))}
paths = []; labels = []
RU.camera(); RU.render('/tmp/_dg_warm_' + os.path.basename(out))
for f in frames:
    sc.frame_set(int(f), subframe=f - int(f))
    for r in regions:
        c = ob.matrix_world @ REG[r]()
        for v in views:
            RU.camera(target=c, d=Vector(V[v]).normalized(), ortho_scale=scale, dist=3)
            p = f'/tmp/_dg_{os.path.basename(out)}_{f}_{r}_{v}.png'; render_bf(p); paths.append(p); labels.append(f'{args[1]} f{f} {r} {v}')
RU.compose(paths, out, cols=len(views) * len(regions) if len(views) * len(regions) <= 6 else len(views), labels=labels)
print('done', out, 'MAGENTA_PX', sum(MAGPX), 'renders', len(MAGPX))
