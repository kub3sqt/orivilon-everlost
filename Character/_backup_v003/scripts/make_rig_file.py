"""Etapa 2a: rig + skinning + nástroje + proxy -> Traveler_Rig_v###.blend"""
import sys, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import rig_lib as R, tools_lib as T
src, dst = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.open_mainfile(filepath=src)
body=bpy.data.objects['Body']
ob=R.build_armature()
R.setup_constraints(ob)
R.calibrate(ob)
R.skin(body, ob)
R.smooth_weights(body)
for s in ('.L','.R'):
    R.joint_blend(body, ob, 'shoulder'+s, 'upper_arm'+s, 0.06, -0.01)
    R.joint_blend(body, ob, 'upper_arm'+s, 'forearm'+s, 0.05)
    R.joint_blend(body, ob, 'forearm'+s, 'hand'+s, 0.03, 0.005)
    R.joint_blend(body, ob, 'hand'+s, 'fingers'+s, 0.035, 0.0)  # v002: měkčí ohyb pěsti
    R.joint_blend(body, ob, 'thigh'+s, 'shin'+s, 0.065)
    R.joint_blend(body, ob, 'shin'+s, 'foot'+s, 0.035)
# v003: twist kosti předloktí. Ve v002 neslo celé přetočení předloktí (při úchopu sekery/krumpáče
# až 100–135°) jen zápěstí -> lineární skinning zápěstí „zaškrtil“ a pod manžetou vznikaly ostré
# plochy. Kost forearm_twist (dítě forearm, od půlky předloktí k zápěstí) přebírá polovinu
# twistu ruky kolem osy předloktí (swing-twist rozklad) a nese distální část předloktí.
R.add_forearm_twist(body, ob)
# nástroje na kosti prop.R
axe=T.make_axe(); pick=T.make_pickaxe()
for t in (axe,pick):
    t.parent=ob; t.parent_type='BONE'; t.parent_bone='prop.R'
    bpy.context.view_layer.update()
    t.matrix_world = ob.matrix_world @ ob.data.bones['prop.R'].matrix_local
pick.hide_viewport=False
# proxy kolekce (nejde do exportu)
col=bpy.data.collections.new('PROXY_targets'); bpy.context.scene.collection.children.link(col)
for o in T.make_proxies():
    for c in o.users_collection: c.objects.unlink(o)
    col.objects.link(o)
for n in ('PROXY_ChopContact','PROXY_MineTarget'):
    o=bpy.data.objects[n]
    for c in o.users_collection: c.objects.unlink(o)
    col.objects.link(o)
bpy.context.scene.render.fps=30
bpy.ops.wm.save_as_mainfile(filepath=dst)
print('saved', dst)
