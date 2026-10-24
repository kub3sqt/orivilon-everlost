"""Pomocné renderování kontrolních náhledů (Workbench/Cycles, ortho kamery)."""
import bpy, math, os
from mathutils import Vector

VIEWS = {  # název: (směr pohledu odkud kamera míří -> kamera stojí na pozici dir*dist)
    'front': Vector((0, -1, 0)), 'side_L': Vector((1, 0, 0)), 'back': Vector((0, 1, 0)),
    'side_R': Vector((-1, 0, 0)), 'q34': Vector((0.75, -1, 0)).normalized(),
}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def setup_workbench(scene=None, silhouette=False, res=(520, 640), cavity=True):
    s = scene or bpy.context.scene
    s.render.engine = 'BLENDER_WORKBENCH'
    s.render.resolution_x, s.render.resolution_y = res
    sh = s.display.shading
    if silhouette:
        sh.light = 'FLAT'; sh.color_type = 'SINGLE'; sh.single_color = (0.12, 0.12, 0.12)
        s.world_color_override = None if False else None
    else:
        sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'
        sh.show_cavity = cavity; sh.cavity_type = 'WORLD'
        sh.show_shadows = False
    s.display_settings.display_device = 'sRGB'
    s.view_settings.view_transform = 'Standard'
    s.render.film_transparent = False
    if s.world is None:
        s.world = bpy.data.worlds.new('W')
    s.world.color = (0.62, 0.64, 0.68) if not silhouette else (1, 1, 1)
    return s


def camera(name='Cam', ortho_scale=1.75, target=Vector((0, 0, 0.76)), d=Vector((0, -1, 0)), dist=6.0, ortho=True, lens=50):
    cam = bpy.data.objects.get(name)
    if cam is None:
        cd = bpy.data.cameras.new(name); cam = bpy.data.objects.new(name, cd)
        bpy.context.scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO' if ortho else 'PERSP'
    cam.data.ortho_scale = ortho_scale; cam.data.lens = lens
    cam.data.clip_end = 100
    cam.location = target + d * dist
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam
    return cam


def render(path):
    s = bpy.context.scene
    s.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def compose(paths, out, cols=None, labels=None, bg=(40, 40, 44)):
    from PIL import Image, ImageDraw
    ims = [Image.open(p).convert('RGB') for p in paths]
    w, h = ims[0].size
    cols = cols or len(ims)
    rows = (len(ims) + cols - 1) // cols
    W = Image.new('RGB', (w * cols, h * rows), bg)
    dr = ImageDraw.Draw(W)
    for i, im in enumerate(ims):
        x, y = (i % cols) * w, (i // cols) * h
        W.paste(im, (x, y))
        if labels:
            dr.text((x + 8, y + 6), labels[i], fill=(20, 20, 20))
    W.save(out)
    return out


def turn_sheet(objs, out, views=('front', 'side_L', 'back', 'q34'), target=Vector((0, 0, 0.76)), ortho_scale=1.75, silhouette=False, res=(420, 560)):
    setup_workbench(silhouette=silhouette, res=res)
    tmp = []
    for v in views:
        camera(target=target, d=VIEWS[v], ortho_scale=ortho_scale)
        p = f'/tmp/_ts_{v}.png'
        render(p); tmp.append(p)
    compose(tmp, out, labels=list(views))
    return out


def setup_cycles(res=(480, 600), samples=24, bg=(0.16, 0.16, 0.18)):
    s = bpy.context.scene
    s.render.engine = 'CYCLES'
    s.cycles.device = 'CPU'; s.cycles.samples = samples
    s.cycles.use_denoising = True
    try:
        s.cycles.denoiser = 'OPENIMAGEDENOISE'
    except Exception:
        pass
    s.render.resolution_x, s.render.resolution_y = res
    s.view_settings.view_transform = 'AgX'
    s.view_settings.exposure = -0.7
    if s.world is None:
        s.world = bpy.data.worlds.new('W')
    s.world.use_nodes = True
    s.world.node_tree.nodes['Background'].inputs[0].default_value = (*bg, 1)
    s.world.node_tree.nodes['Background'].inputs[1].default_value = 0.35
    def L(name, loc, energy, size):
        ld = bpy.data.lights.get(name) or bpy.data.lights.new(name, 'AREA')
        ld.energy = energy; ld.size = size
        o = bpy.data.objects.get(name) or bpy.data.objects.new(name, ld)
        if o.name not in s.collection.objects:
            s.collection.objects.link(o)
        o.location = loc
        o.rotation_euler = (Vector((0, 0, 0.8)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    L('Key', (-2.0, -3.0, 3.2), 260, 2.5)
    L('Fill', (3.0, -2.0, 1.5), 70, 3.0)
    L('Rim', (1.0, 3.5, 2.8), 220, 2.0)
    return s


def ivory_material(name='Ivory', col=(0.86, 0.83, 0.78)):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*col, 1)
    b.inputs['Roughness'].default_value = 0.55
    try:
        b.inputs['Subsurface Weight'].default_value = 0.08
    except Exception:
        pass
    m.diffuse_color = (*col, 1)
    return m


def add_floor(size=6, col=(0.28, 0.28, 0.30)):
    import bmesh
    fl = bpy.data.meshes.new('StudioFloor'); bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=size); bm.to_mesh(fl); bm.free()
    f = bpy.data.objects.new('StudioFloor', fl); bpy.context.scene.collection.objects.link(f)
    m = bpy.data.materials.new('FloorMat'); m.use_nodes = True
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*col, 1)
    m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.8
    m.diffuse_color = (*col, 1)
    fl.materials.append(m)
    return f
