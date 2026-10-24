"""Náhledový GLB (jen pro prohlížení, NE herní export): postava + bílá varianta + nástroje
na kosti prop.R + cílové proxy (kmen, zem). Všechny zapečené klipy jako glTF animace."""
import sys, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import tools_lib as T
from mathutils import Matrix
src, out = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.open_mainfile(filepath=src)
exp = bpy.data.objects['Traveler']
for n in ('Tool_Axe', 'Tool_Pickaxe'):
    t = bpy.data.objects[n]
    t.parent = exp; t.parent_type = 'BONE'; t.parent_bone = 'prop.R'
    bpy.context.view_layer.update()
    t.matrix_world = exp.matrix_world @ exp.data.bones['prop.R'].matrix_local
trunk, ground = T.make_proxies()
for o in list(bpy.data.objects):
    if o.type == 'EMPTY':
        bpy.data.objects.remove(o)
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_animations=True,
                          export_animation_mode='ACTIONS', export_force_sampling=True, export_frame_step=1,
                          export_skins=True, export_def_bones=True, export_yup=True, export_apply=False,
                          export_extras=True, export_cameras=False, export_lights=False)
print('GLB OK', out)
