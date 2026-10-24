"""Ověření exportu: import FBX/GLB do čisté scény, kontrola kostí, klipů, měřítka, normál a shody
pozic klíčových kostí se zdrojovou (zapečenou) kostrou na vybraných snímcích."""
import sys, json, bpy
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
ref_blend, path = args[0], args[1]
bones = ['hips', 'head', 'hand.R', 'hand.L', 'foot.L', 'foot.R', 'prop.R']
# reference
bpy.ops.wm.open_mainfile(filepath=ref_blend)
ref = bpy.data.objects['Traveler']
refpos = {}
src_start = {}
for a in bpy.data.actions:
    ref.animation_data.action = a
    src_start[a.name] = int(a.frame_start)
    for f in (int(a.frame_start), int((a.frame_start + a.frame_end) // 2), int(a.frame_end) - 1):
        bpy.context.scene.frame_set(f)
        refpos[(a.name, f)] = {b: (ref.matrix_world @ ref.pose.bones[b].head).copy() for b in bones}
# čistá scéna + import
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = 30
if path.endswith('.fbx'):
    bpy.ops.import_scene.fbx(filepath=path, axis_forward='-Z', axis_up='Y', use_anim=True, automatic_bone_orientation=False, ignore_leaf_bones=True)
else:
    bpy.ops.import_scene.gltf(filepath=path)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
rep = {'file': path.split('/')[-1], 'armature': arm.name, 'bones': len(arm.data.bones),
       'meshes': {m.name: len(m.data.vertices) for m in meshes},
       'actions': {a.name: [a.frame_range[0], a.frame_range[1]] for a in bpy.data.actions}}
dg = bpy.context.evaluated_depsgraph_get()
zs = []
for m in meshes:
    for v in m.data.vertices:
        zs.append((m.matrix_world @ v.co).z)
rep['rest_height_m'] = round(max(zs) - min(zs), 3)
rep['materials'] = sorted({s.material.name for m in meshes for s in m.material_slots if s.material})
# normály: na uzavřeném těle porovnej s přepočtem 'ven' (podíl převrácených ploch)
import bmesh
flip = {}
for m in meshes:
    if not m.name.startswith('Body'):
        continue
    bm = bmesh.new(); bm.from_mesh(m.data)
    before = [f.normal.copy() for f in bm.faces]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    flip[m.name] = round(sum(1 for f, n in zip(bm.faces, before) if f.normal.dot(n) < 0) / len(before), 4)
    bm.free()
rep['body_flipped_face_ratio'] = flip
# shoda pozic
errs = {}
acts = {a.name.split('|')[-1]: a for a in bpy.data.actions}
for (an, f), pos in refpos.items():
    key = next((k for k in acts if k == an or k.endswith(an) or an in k), None)
    if key is None:
        errs[an] = 'chybí'; continue
    arm.animation_data.action = acts[key]
    if hasattr(arm.animation_data, 'action_slot') and acts[key].slots:
        arm.animation_data.action_slot = acts[key].slots[0]
    off = int(acts[key].frame_range[0]) - src_start[an]
    bpy.context.scene.frame_set(f + off)
    e = max((arm.matrix_world @ arm.pose.bones[b].head - p).length for b, p in pos.items() if b in arm.pose.bones)
    errs[an] = max(errs.get(an, 0) if not isinstance(errs.get(an), str) else 0, round(e * 1000, 2))
rep['max_bone_pos_err_mm_vs_source'] = errs
print('VERIFY ' + json.dumps(rep, ensure_ascii=False))
