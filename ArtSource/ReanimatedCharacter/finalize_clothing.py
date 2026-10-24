import bpy,os
OUT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=OUT+'/AnimatedWanderer.blend',load_ui=False)
r=bpy.data.objects['Character_Rig'];o=bpy.data.objects['Reference_Character']
# Hide the body surface underneath opaque garments, keeping the source mesh intact.
# This prevents exposed white pixels when knees and elbows compress the clothing.
g=o.vertex_groups.get('Visible skin') or o.vertex_groups.new(name='Visible skin')
armgroups={g.index for g in o.vertex_groups if g.name.startswith(('upper_arm.','forearm.','hand.','thumb.'))}
ids=[v.index for v in o.data.vertices if v.co.z>3.78 or sum(g.weight for g in v.groups if g.group in armgroups)>.18]
g.add(ids,1,'REPLACE');m=o.modifiers.get('Body beneath clothing') or o.modifiers.new('Body beneath clothing','MASK');m.vertex_group=g.name
bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_move_to_index(modifier=m.name,index=0)
for t in r.animation_data.nla_tracks:t.mute=t.name!='Walk'
bpy.context.scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/AnimatedWanderer.blend')
bpy.ops.object.select_all(action='DESELECT')
for obj in bpy.context.scene.objects:
 if obj==r or obj.parent==r:obj.select_set(True)
bpy.context.view_layer.objects.active=r
for t in r.animation_data.nla_tracks:t.mute=False
bpy.ops.export_scene.gltf(filepath=OUT+'/AnimatedWanderer.glb',use_selection=True,export_format='GLB',export_animations=True,export_animation_mode='NLA_TRACKS',export_skins=True)
bpy.ops.export_scene.fbx(filepath=OUT+'/AnimatedWanderer.fbx',use_selection=True,add_leaf_bones=False,bake_anim=True,bake_anim_use_nla_strips=True,bake_anim_use_all_actions=False,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',object_types={'MESH','ARMATURE'})
