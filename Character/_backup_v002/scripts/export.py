"""Export: čistá herní postava (FBX pro Unity), samostatné nástroje (FBX), náhledový GLB.

Postup:
 1. Z authoring rigu (IK, foot roll, constrainty, drivery) se KAŽDÝ snímek každé akce zapéká
    do běžných lokálních transformací deformačních kostí exportní kostry 'Traveler'
    (žádné constrainty, drivery ani skripty – čisté klíče).
 2. Meshe: modifikátory (Solidify, Mask, Subsurf) se aplikují, zůstává jen Armature.
    Skupiny pro lokální skrývání: Body, Body_Head, Outfit_Hat, Outfit_Scarf, Outfit_Torso,
    Outfit_Legs, Outfit_Wrists.
 3. FBX: -Z forward, Y up, jednotky m (scale 1.0), jen deform kosti, bez leaf bones.
python3 export.py -- dressed.blend out_dir
"""
import sys, os, math, bpy
from mathutils import Matrix
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import anim_lib as A

src, out = sys.argv[-2], sys.argv[-1]
os.makedirs(out, exist_ok=True)
CLIPS = ['Idle', 'Walk', 'Run', 'Chop', 'Mine', 'Swim']

bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene
sc.render.fps = 30
rig = bpy.data.objects['Traveler_Rig']
DEFORM = [b.name for b in rig.data.bones if b.use_deform or b.name == 'root']


def ensure_mode(ob, mode):
    bpy.context.view_layer.objects.active = ob
    if ob.mode != mode:
        bpy.ops.object.mode_set(mode=mode)


# ------------------------------------------------------------------ exportní kostra
def make_export_armature(name='Traveler'):
    ad = rig.data.copy(); ad.name = name
    ob = bpy.data.objects.new(name, ad)
    sc.collection.objects.link(ob)
    ensure_mode(ob, 'EDIT')
    for eb in list(ad.edit_bones):
        if eb.name not in DEFORM:
            ad.edit_bones.remove(eb)
    root = ad.edit_bones['root']; root.use_deform = True
    ensure_mode(ob, 'OBJECT')
    for pb in ob.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
        pb.rotation_mode = 'QUATERNION'
    if ob.animation_data:
        ob.animation_data_clear()
    return ob


exp = make_export_armature()


def bake_clip(name):
    act = bpy.data.actions[name]
    rig.animation_data.action = act
    f0, f1 = int(act.frame_start), int(act.frame_end)
    frames = list(range(f0, f1 + 1))
    data = A.bake_deform(rig, act, frames)
    new = bpy.data.actions.new(name + '_bake')
    exp.animation_data_create(); exp.animation_data.action = new
    for f in frames:
        for bn, M in data[f].items():
            pb = exp.pose.bones.get(bn)
            if pb is None:
                continue
            loc, rot, scl = M.decompose()
            pb.location = loc; pb.rotation_quaternion = rot; pb.scale = (1, 1, 1)
        # kontinuita kvaternionů
        for pb in exp.pose.bones:
            q = pb.rotation_quaternion
            prev = getattr(bake_clip, '_prev', {}).get(pb.name)
            if prev is not None and prev.dot(q) < 0:
                q.negate(); pb.rotation_quaternion = q
            bake_clip._prev = getattr(bake_clip, '_prev', {}); bake_clip._prev[pb.name] = q.copy()
            pb.keyframe_insert('location', frame=f, group=pb.name)
            pb.keyframe_insert('rotation_quaternion', frame=f, group=pb.name)
    for fc in A.fcurves_of(new):
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'
    new.use_frame_range = True; new.frame_start = f0; new.frame_end = f1
    new.use_fake_user = True
    for k in ('in_place', 'speed_mps', 'stride_m', 'tool', 'contact_frame', 'water_level_z', 'recommended_swim_speed_mps'):
        if k in act.keys():
            new[k] = act[k]
    bake_clip._prev = {}
    return new


baked = {n: bake_clip(n) for n in CLIPS}
rig.animation_data.action = None

# ------------------------------------------------------------------ meshe
GROUPS = {
    'Outfit_Hat': ['Outfit_Hat', 'Outfit_HatBand', 'Outfit_HatPatch1'],
    'Outfit_Scarf': ['Outfit_ScarfWrap', 'Outfit_ScarfKnot', 'Outfit_ScarfTailFront', 'Outfit_ScarfTailFront2'],
    'Outfit_Torso': ['Outfit_Tunic', 'Outfit_TunicSkirt', 'Outfit_Mantle', 'Outfit_Belt', 'Outfit_BeltBuckle', 'Outfit_Strap', 'Outfit_Bag'],
    'Outfit_Legs': ['Outfit_Pants', 'Outfit_PantsPatch_KneeL', 'Outfit_PantsPatch_ThighR', 'Outfit_PantsPatch_BackL', 'Outfit_Boots', 'Outfit_BootCuff.L', 'Outfit_BootCuff.R'],
    'Outfit_Wrists': ['Outfit_WristBand.L', 'Outfit_WristBand.R'],
}


def baked_copy(src_ob, name, keep_mask=True):
    """Kopie s aplikovanými modifikátory (kromě Armature), navázaná na exportní kostru."""
    ob = src_ob.copy(); ob.data = src_ob.data.copy(); ob.name = name; ob.data.name = name
    sc.collection.objects.link(ob)
    if not keep_mask:
        for m in list(ob.modifiers):
            if m.type == 'MASK':
                ob.modifiers.remove(m)
    ensure_mode(ob, 'OBJECT')
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    for m in list(ob.modifiers):
        if m.type == 'ARMATURE':
            continue
        bpy.ops.object.modifier_apply(modifier=m.name)
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    ob.parent = exp; ob.matrix_parent_inverse = Matrix()
    am = ob.modifiers.new('Armature', 'ARMATURE'); am.object = exp
    # odstraň prázdné/ne-deform skupiny
    for g in list(ob.vertex_groups):
        if g.name not in DEFORM:
            ob.vertex_groups.remove(g)
    return ob


def join(objs, name):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name; ob.data.name = name
    return ob


def split_head(body_ob):
    """Oddělí hlavu (+ horní krk) do Body_Head – pro skrytí v první osobě."""
    import bmesh
    groups = {g.index: g.name for g in body_ob.vertex_groups}
    me = body_ob.data
    headv = set()
    for v in me.vertices:
        if v.groups:
            g = max(v.groups, key=lambda x: x.weight)
            if groups[g.group] == 'head':
                headv.add(v.index)
    ensure_mode(body_ob, 'EDIT')
    bm = bmesh.from_edit_mesh(me)
    for f in bm.faces:
        f.select = all(v.index in headv for v in f.verts)
    bmesh.update_edit_mesh(me)
    bpy.ops.mesh.separate(type='SELECTED')
    ensure_mode(body_ob, 'OBJECT')
    head = [o for o in bpy.context.selected_objects if o != body_ob][0]
    head.name = body_ob.name + '_Head'; head.data.name = head.name
    return head


body_src = bpy.data.objects['Body']
body_white = baked_copy(body_src, 'Body_White', keep_mask=False)
body_dressed = baked_copy(body_src, 'Body', keep_mask=True)
head_dressed = split_head(body_dressed)
parts = {}
for gname, names in GROUPS.items():
    objs = [baked_copy(bpy.data.objects[n], n + '_x') for n in names]
    parts[gname] = join(objs, gname)

# ------------------------------------------------------------------ úklid authoring dat
keep = {exp.name, body_white.name, body_dressed.name, head_dressed.name, *[p.name for p in parts.values()]}
tools_src = {n: bpy.data.objects[n] for n in ('Tool_Axe', 'Tool_Pickaxe')}


def write_fbx(path, objs, actions=True):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='-Z', axis_up='Y', use_space_transform=True, bake_space_transform=False,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=False,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        use_armature_deform_only=True, armature_nodetype='NULL',
        bake_anim=actions, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=actions, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0, path_mode='AUTO', embed_textures=False)


# akce pro export: jen zapečené, s čistými jmény
for a in list(bpy.data.actions):
    if not a.name.endswith('_bake'):
        a.use_fake_user = False
        bpy.data.actions.remove(a)
for n, a in baked.items():
    a.name = n
exp.animation_data.action = baked['Idle']
rig_obj = bpy.data.objects.get('Traveler_Rig')
# odstranit authoring objekty, ať exportéry vidí jen čistou postavu
for o in list(bpy.data.objects):
    if o.name not in keep and o.name not in tools_src:
        bpy.data.objects.remove(o, do_unlink=True)

body_dressed.name = 'Body'; body_dressed.data.name = 'Body'
head_dressed.name = 'Body_Head'; head_dressed.data.name = 'Body_Head'
for gname, p in parts.items():
    p.name = gname; p.data.name = gname
dressed_objs = [exp, body_dressed, head_dressed] + list(parts.values())
body_white.hide_set(True)
write_fbx(os.path.join(out, 'Everlost_Traveler_Default.fbx'), dressed_objs)
body_white.hide_set(False)
write_fbx(os.path.join(out, 'Everlost_Traveler_WhiteBody.fbx'), [exp, body_white])

# bílá verze (bez oblečení, bez masky) pro hodnocení těla
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, '_export_scene_dressed.blend'))

# ------------------------------------------------------------------ nástroje zvlášť (počátek = úchop pravé ruky)
for n, t in tools_src.items():
    t.parent = None; t.matrix_world = Matrix(); t.modifiers.clear() if False else None
    write_fbx(os.path.join(out, n + '.fbx'), [t], actions=False)
print('EXPORT OK', out)
