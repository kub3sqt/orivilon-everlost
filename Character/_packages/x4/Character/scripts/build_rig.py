import sys, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import rig_lib as R, render_util as RU
bpy.ops.wm.open_mainfile(filepath=sys.argv[-2])
body=bpy.data.objects['Body']
ob=R.build_armature()
R.setup_constraints(ob)
R.calibrate(ob)
R.skin(body, ob)
print('groups', len(body.vertex_groups))
bpy.ops.wm.save_as_mainfile(filepath=sys.argv[-1])
