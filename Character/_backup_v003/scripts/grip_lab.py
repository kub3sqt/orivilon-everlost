"""Laboratoř úchopu: pravá i levá ruka na násadě v rest póze (nástroj v gripu), detailní pohledy + měření
skutečné geometrie: pokrytí obvodu násady dlaní/prsty a průnik."""
import sys, os, math, bpy, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_util as RU, rig_lib as R
from mathutils import Vector, Matrix
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0]); out = args[1]
fing = float(args[2]) if len(args) > 2 else 48; thumb = float(args[3]) if len(args) > 3 else 25
ob = bpy.data.objects['Traveler_Rig']; P = ob.pose.bones
if ob.animation_data: ob.animation_data.action = None
for pb in P:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
for s in ('.L', '.R'):
    P['fingers' + s].rotation_quaternion = Matrix.Rotation(math.radians(fing), 4, 'X').to_quaternion()
    P['thumb' + s].rotation_quaternion = Matrix.Rotation(math.radians(thumb), 4, 'X').to_quaternion()
# levá ruka: IK na MCH_grip.L nechceme (rest) -> měříme pravou ruku v rest; levou přes zrcadlo
bpy.context.view_layer.update()
for o in bpy.data.objects:
    if o.name.startswith('PROXY') or o.name == 'Tool_Pickaxe': o.hide_render = True
body = bpy.data.objects['Body']
dg = bpy.context.evaluated_depsgraph_get(); be = body.evaluated_get(dg); me = be.to_mesh()
co = np.array([(be.matrix_world @ v.co)[:] for v in me.vertices]); be.to_mesh_clear()
T = ob.matrix_world @ ob.data.bones['CTRL_tool'].matrix_local  # rám nástroje (grip pravé ruky)
Ti = T.inverted()
loc = np.array([(Ti @ Vector(c))[:] for c in co])
# body ruky v pásmu násady |y| < 0.045 kolem gripu pravé ruky
band = loc[np.abs(loc[:, 1]) < 0.045]
r = np.hypot(band[:, 0], band[:, 2])
near = band[r < 0.0175 + 0.012]
ang = np.degrees(np.arctan2(near[:, 2], near[:, 0]))
cov = len(set((ang // 15).astype(int))) * 15 if len(near) else 0
pen = max(0.0, 0.0175 - r.min()) if len(r) else 0
print(f'GRIP fingers={fing} thumb={thumb} coverage_deg={cov} min_r_mm={r.min()*1000:.1f} penetration_mm={pen*1000:.1f} n_near={len(near)}')
RU.setup_workbench(res=(420, 420))
bpy.context.scene.display.shading.show_shadows = False
c = T.translation
paths = []
for i, d in enumerate([T.col[2].xyz, -T.col[2].xyz, T.col[0].xyz, T.col[1].xyz + T.col[0].xyz * 0.5]):
    RU.camera(target=c, d=Vector(d).normalized(), ortho_scale=0.28); p = f'/tmp/_gl{i}.png'; RU.render(p); paths.append(p)
RU.compose(paths, out, labels=['-Z', '+Z', 'ostri', 'podel'])
