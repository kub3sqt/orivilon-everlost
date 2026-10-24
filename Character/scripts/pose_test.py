import sys, bpy, math
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import render_util as RU, pose_lib as PL
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=sys.argv[-2])
ob=bpy.data.objects['Traveler_Rig']; body=bpy.data.objects['Body']
body.data.materials.clear(); m=bpy.data.materials.new('b'); m.diffuse_color=(0.85,0.83,0.78,1); body.data.materials.append(m)
ob.hide_render=True
P=ob.pose.bones
def squat():
    PL.move_world(P['hips'],(0,0.07,-0.20)); PL.rot_world(P['hips'],PL.eul(18)); PL.rot_world(P['spine'],PL.eul(8)); PL.rot_world(P['chest'],PL.eul(4))
    for s,x in (('.L',1),('.R',-1)): PL.swing_to(P['upper_arm'+s],(0.25*x,-0.9,-0.35)); PL.local_x(P['forearm'+s],20)
def bend():
    PL.move_world(P['hips'],(0,0.07,-0.03)); PL.rot_world(P['hips'],PL.eul(30)); PL.rot_world(P['spine'],PL.eul(25)); PL.rot_world(P['chest'],PL.eul(20)); PL.rot_world(P['neck'],PL.eul(-15))
def elbow():
    for s,x in (('.L',1),('.R',-1)): PL.swing_to(P['upper_arm'+s],(0.15*x,-0.1,-1)); PL.local_x(P['forearm'+s],125)
def arms_fwd():
    for s,x in (('.L',1),('.R',-1)): PL.swing_to(P['upper_arm'+s],(0.12*x,-1,0.0)); PL.rot_world(P['shoulder'+s],PL.eul(0,-8*x,-12*x))
def arms_up():
    for s,x in (('.L',1),('.R',-1)): PL.swing_to(P['upper_arm'+s],(0.3*x,-0.15,1.0)); PL.rot_world(P['shoulder'+s],PL.eul(0,-25*x,0))
def twist():
    PL.rot_world(P['spine'],PL.eul(0,0,20)); PL.rot_world(P['chest'],PL.eul(0,0,25))
    PL.swing_to(P['upper_arm.R'],(0.35,-0.8,-0.3)); PL.local_x(P['forearm.R'],60)
    PL.swing_to(P['upper_arm.L'],(0.4,0.6,-0.5)); PL.local_x(P['forearm.L'],70)
poses=[('squat',squat),('bend',bend),('elbow',elbow),('arms_fwd',arms_fwd),('arms_up',arms_up),('twist',twist)]
RU.setup_workbench(res=(330,420))
out=[]
for name,fn in poses:
    PL.clear(ob); fn(); PL.update()
    for v in ('front','side_L','q34'):
        RU.camera(target=Vector((0,0,0.72)),d=RU.VIEWS[v]+Vector((0,0,0.25)),ortho_scale=1.75)
        p=f'/tmp/_pt_{name}_{v}.png'; RU.render(p); out.append(p)
RU.compose(out, sys.argv[-1], cols=6, labels=[f'{n} {v}' for n,_ in poses for v in ('F','S','Q')])
