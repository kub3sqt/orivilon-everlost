"""Kontrolní přehrání akce: snímky ze 3 úhlů -> MP4 (1x a 0.5x) + kontaktní arch + grafy chodidel.

Použití: python3 review_anim.py -- file.blend Action out_dir [travel] [tool=Axe|Pickaxe|none]
 travel: posouvá celou postavu dopředu rychlostí akce (podlaha se šachovnicí) – odhalí klouzání
 a obrácený směr chůze, který by in-place náhled skryl.
"""
import sys, os, math, subprocess, bpy, bmesh
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import render_util as RU, anim_lib as A
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
src, act_name, out_dir = args[0], args[1], args[2]
travel = 'travel' in args
tool = next((a.split('=')[1] for a in args if a.startswith('tool=')), 'none')
views = next((a.split('=')[1] for a in args if a.startswith('views=')), 'side,front,q34').split(',')
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=src)
ob = bpy.data.objects['Traveler_Rig']
act = bpy.data.actions[act_name]
ob.animation_data.action = act
ob.hide_render = True
f0, f1 = int(act.frame_start), int(act.frame_end)
speed = act.get('speed_mps', 0.0)
for t in ('Tool_Axe', 'Tool_Pickaxe'):
    o = bpy.data.objects.get(t)
    if o:
        o.hide_render = (t != 'Tool_' + tool)
show_proxy = tool != 'none' and 'noproxy' not in args
for o in bpy.data.objects:
    if o.name.startswith('PROXY_'):
        o.hide_render = not show_proxy
    if o.name.startswith('PROXY_Ground'):
        o.hide_render = True
# šachovnicová podlaha (10 cm)
me = bpy.data.meshes.new('chk'); bm = bmesh.new()
n = 40; s = 0.1
m1 = bpy.data.materials.new('c1'); m1.diffuse_color = (0.55, 0.56, 0.58, 1)
m2 = bpy.data.materials.new('c2'); m2.diffuse_color = (0.42, 0.43, 0.46, 1)
for i in range(-n // 2, n // 2):
    for j in range(-n // 2, n // 2):
        vs = [bm.verts.new((i * s, j * s, 0)), bm.verts.new(((i + 1) * s, j * s, 0)),
              bm.verts.new(((i + 1) * s, (j + 1) * s, 0)), bm.verts.new((i * s, (j + 1) * s, 0))]
        f = bm.faces.new(vs); f.material_index = (i + j) % 2
bm.to_mesh(me); fl = bpy.data.objects.new('CheckerFloor', me); fl.data.materials.append(m1); fl.data.materials.append(m2)
bpy.context.scene.collection.objects.link(fl)
for o in bpy.data.objects:
    if o.type == 'MESH' and o.name == 'Body':
        mb = bpy.data.materials.new('wb'); mb.diffuse_color = (0.86, 0.84, 0.79, 1)
        o.data.materials.clear(); o.data.materials.append(mb)

RU.setup_workbench(res=(400, 440))
sc = bpy.context.scene
sc.display.shading.show_shadows = True
VIEW = {'side': Vector((1, 0, 0.08)), 'front': Vector((0, -1, 0.10)), 'q34': Vector((0.8, -0.9, 0.35)),
        'back': Vector((0, 1, 0.1)), 'top': Vector((0.001, 0.0, 1)), 'sideR': Vector((-1, 0, 0.08)), 'q34r': Vector((-0.9, -0.7, 0.45)), 'q34b': Vector((-0.7, 0.8, 0.5)), 'q34rb': Vector((-0.8, 0.7, 0.4)), 'under': Vector((0.3, -0.5, -0.8))}
tmp = f'/tmp/_ra_{act_name}'; os.makedirs(tmp, exist_ok=True)
for f in os.listdir(tmp):
    os.remove(os.path.join(tmp, f))
frames = list(range(f0, f1))  # poslední = první (smyčka)
only = next((a.split('=')[1] for a in args if a.startswith('frames=')), None)
if only:
    frames = [int(x) for x in only.split(',')]
target_z = float(next((a.split('=')[1] for a in args if a.startswith('tz=')), 0.62))
oscale = float(next((a.split('=')[1] for a in args if a.startswith('os=')), 1.9))
for k, f in enumerate(frames):
    sc.frame_set(f)
    dy = -speed * (f - f0) / A.FPS if travel else 0.0
    ob.location.y = dy
    for o in bpy.data.objects:
        if o.name.startswith('PROXY_'):
            pass
    sc.frame_set(f)
    paths = []
    for v in views:
        RU.camera(target=Vector((0, dy, target_z)), d=VIEW[v].normalized(), ortho_scale=oscale)
        p = f'{tmp}/{v}_{k:03d}.png'; RU.render(p); paths.append(p)
    RU.compose(paths, f'{tmp}/f_{k:03d}.png', labels=[f'{act_name} {v} f{f}' for v in views])
name = act_name + ('_travel' if travel else '') + ('_keys' if only else '')
if only:
    RU.compose([f'{tmp}/f_{k:03d}.png' for k in range(len(frames))], f'{out_dir}/{name}_grid.png', cols=2)
    print('done grid'); raise SystemExit
for spd, tag in ((1.0, ''), (0.5, '_half')):
    fps = A.FPS * spd
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-stream_loop', '2', '-framerate', str(fps), '-i', f'{tmp}/f_%03d.png',
                    '-pix_fmt', 'yuv420p', '-vf', 'pad=ceil(iw/2)*2:ceil(ih/2)*2', f'{out_dir}/{name}{tag}.mp4'])
# kontaktní arch: každý n-tý snímek
from PIL import Image
step = max(1, len(frames) // 8)
sel = [f'{tmp}/f_{k:03d}.png' for k in range(0, len(frames), step)]
ims = [Image.open(p) for p in sel]
W = Image.new('RGB', (ims[0].width * 2, ims[0].height * ((len(ims) + 1) // 2)))
for i, im in enumerate(ims):
    W.paste(im, ((i % 2) * im.width, (i // 2) * im.height))
W.save(f'{out_dir}/{name}_sheet.png')
print('done', name, len(frames))
