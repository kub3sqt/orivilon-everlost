import sys, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import render_util as RU
from mathutils import Vector
args=sys.argv[sys.argv.index('--')+1:]
bpy.ops.wm.open_mainfile(filepath=args[0])
for o in bpy.data.objects:
    if o.name.startswith(('PROXY_','Tool_')) or o.type=='ARMATURE': o.hide_render=True
for c in bpy.data.collections:
    if c.name.startswith('PROXY'): c.hide_render=True
ob=bpy.data.objects['Traveler_Rig']
act=args[2] if len(args)>2 and args[2]!='rest' else None
fr=int(args[3]) if len(args)>3 else 0
if act:
    ob.animation_data.action=bpy.data.actions[act]
    t=bpy.data.actions[act].get('tool')
    if t: bpy.data.objects['Tool_'+t].hide_render=False
else:
    ob.animation_data.action=None
    for pb in ob.pose.bones: pb.location=(0,0,0); pb.rotation_quaternion=(1,0,0,0)
bpy.context.scene.frame_set(fr)
b=bpy.data.objects['Body']; b.data.materials.clear(); b.data.materials.append(RU.ivory_material())
RU.add_floor()
RU.setup_cycles(res=(420,600), samples=28)
out=[]
views=args[4].split(',') if len(args)>4 else ['front','q34','side_L','back']
for v in views:
    RU.camera(target=Vector((0,0,0.78)), d=RU.VIEWS[v]+Vector((0,0,0.10)), ortho=False, lens=85, dist=5.6)
    p=f'/tmp/_bd_{v}.png'; RU.render(p); out.append(p)
RU.compose(out, args[1])
