import sys, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import rig_lib as R
bpy.ops.wm.open_mainfile(filepath=sys.argv[-2])
body=bpy.data.objects['Body']; arm=bpy.data.objects['Traveler_Rig']
R.smooth_weights(body)
for s in ('.L','.R'):
    R.joint_blend(body, arm, 'shoulder'+s, 'upper_arm'+s, 0.06, -0.01)
    R.joint_blend(body, arm, 'upper_arm'+s, 'forearm'+s, 0.05)
    R.joint_blend(body, arm, 'forearm'+s, 'hand'+s, 0.03, 0.005)
    R.joint_blend(body, arm, 'thigh'+s, 'shin'+s, 0.065)
    R.joint_blend(body, arm, 'shin'+s, 'foot'+s, 0.035)
bpy.ops.wm.save_as_mainfile(filepath=sys.argv[-1])
