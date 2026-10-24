"""Nástroje (sekera, krumpáč) a cílové proxy (kmen, zem).

Prostor nástroje (= kost prop.R / CTRL_tool):
  počátek = střed násady v úchopu PRAVÉ (horní) ruky,
  +Y = podél násady směrem k hlavě nástroje,
  +X = směr ostří sekery / úderného hrotu krumpáče,
  levá (spodní) ruka drží násadu v y = TOOL_GRIP_L_Y (-0.165 m).
"""
import math, bpy, bmesh
from mathutils import Vector, Matrix

HANDLE_R = 0.0175


def mat(name, col, rough=0.6, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*col, 1)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    m.diffuse_color = (*col, 1)
    return m


def handle(bm, y0, y1, r0, r1, seg=12, rings=10, knob=True):
    """Násada: mírně soudkovitá, zúžená, s hlavicí na konci (knob)."""
    prev = None
    first_ring = None
    profile = []
    for i in range(rings + 1):
        t = i / rings
        y = y0 + (y1 - y0) * t
        r = r0 + (r1 - r0) * t
        if knob and t < 0.08:
            r *= 1.25 - 3.0 * t
        profile.append((y, r))
    rings_v = []
    for (y, r) in profile:
        ring = [bm.verts.new((r * math.cos(a), y, r * math.sin(a)))
                for a in [2 * math.pi * k / seg for k in range(seg)]]
        rings_v.append(ring)
    for a, b in zip(rings_v[:-1], rings_v[1:]):
        for k in range(seg):
            bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
    bm.faces.new(list(reversed(rings_v[0])))
    bm.faces.new(rings_v[-1])


def extrude_profile(bm, pts2d, depth, xy_to_world):
    """Plochý tvar (seznam (x,y)) vytažený v ose Z o +-depth/2 (konvexní/ hvězdicový obrys)."""
    top = [bm.verts.new(xy_to_world(x, y, depth / 2)) for x, y in pts2d]
    bot = [bm.verts.new(xy_to_world(x, y, -depth / 2)) for x, y in pts2d]
    n = len(pts2d)
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    for k in range(n):
        bm.faces.new((bot[k], bot[(k + 1) % n], top[(k + 1) % n], top[k]))


def finish(bm, name, mats_by_face=None):
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def bevel_obj(ob, w=0.004, seg=2):
    m = ob.modifiers.new('Bevel', 'BEVEL'); m.width = w; m.segments = seg; m.limit_method = 'ANGLE'
    m.angle_limit = math.radians(35)
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.ops.object.modifier_apply(modifier='Bevel')
    bpy.ops.object.shade_auto_smooth(angle=math.radians(40))


def make_axe():
    wood = mat('M_Tool_Wood', (0.32, 0.18, 0.09), 0.7)
    iron = mat('M_Tool_Iron', (0.36, 0.37, 0.39), 0.38, 0.85)
    edge = mat('M_Tool_Edge', (0.72, 0.73, 0.74), 0.25, 0.95)
    # násada
    bm = bmesh.new()
    handle(bm, -0.215, 0.455, HANDLE_R * 1.05, HANDLE_R * 0.92)
    h = finish(bm, 'Axe_Handle'); h.data.materials.append(wood)
    for p in h.data.polygons:
        p.use_smooth = True
    # hlava: profil v rovině X-Y (ostří ve směru +X), tloušťka v Z
    prof = [(-0.030, 0.355), (0.020, 0.360), (0.070, 0.335), (0.118, 0.300), (0.132, 0.380),
            (0.134, 0.430), (0.118, 0.468), (0.070, 0.440), (0.020, 0.425), (-0.030, 0.428),
            (-0.042, 0.405), (-0.042, 0.378)]
    bm = bmesh.new()
    extrude_profile(bm, prof, 0.034, lambda x, y, z: (x, y, z))
    hd = finish(bm, 'Axe_Head')
    # zúžení ostří: vrcholy s x > 0.09 stáhnout v Z
    for v in hd.data.vertices:
        if v.co.x > 0.085:
            k = min((v.co.x - 0.085) / 0.05, 1.0)
            v.co.z *= (1.0 - 0.85 * k)
    hd.data.materials.append(iron); hd.data.materials.append(edge)
    for p in hd.data.polygons:
        c = sum((hd.data.vertices[i].co for i in p.vertices), Vector()) / len(p.vertices)
        p.material_index = 1 if c.x > 0.105 else 0
    bevel_obj(hd, 0.003, 2)
    return join([h, hd], 'Tool_Axe')


def make_pickaxe():
    wood = mat('M_Tool_Wood', (0.32, 0.18, 0.09), 0.7)
    iron = mat('M_Tool_Iron', (0.36, 0.37, 0.39), 0.38, 0.85)
    bm = bmesh.new()
    handle(bm, -0.215, 0.545, HANDLE_R * 1.05, HANDLE_R * 0.95)
    h = finish(bm, 'Pick_Handle'); h.data.materials.append(wood)
    for p in h.data.polygons:
        p.use_smooth = True
    # hlava: zahnutý rohový tvar – úderný hrot +X (delší), plochý hrot -X
    bm = bmesh.new()
    seg = 8
    pts = []
    N = 14
    for i in range(N + 1):
        t = -1 + 2 * i / N  # -1..1
        x = 0.185 * t if t > 0 else 0.150 * t
        y = 0.520 - 0.055 * (t * t)  # zahnutí dolů (k ruce) na koncích
        r = 0.026 * (1 - 0.88 * abs(t) ** 1.4)
        if abs(t) < 0.18:
            r = 0.030  # oko násady
        pts.append((Vector((x, y, 0)), r, t))
    rings = []
    for i, (c, r, t) in enumerate(pts):
        tan = (pts[min(i + 1, N)][0] - pts[max(i - 1, 0)][0]).normalized()
        n1 = Vector((0, 0, 1)); n2 = tan.cross(n1).normalized()
        flat = 0.65 if t < 0 else 1.0  # plochý (adze) konec
        ring = [bm.verts.new(c + n1 * math.cos(a) * r * (1.15) + n2 * math.sin(a) * r * flat)
                for a in [2 * math.pi * k / seg for k in range(seg)]]
        rings.append(ring)
    for a, b in zip(rings[:-1], rings[1:]):
        for k in range(seg):
            bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    hd = finish(bm, 'Pick_Head'); hd.data.materials.append(iron)
    for p in hd.data.polygons:
        p.use_smooth = True
    return join([h, hd], 'Tool_Pickaxe')


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


# ---- cílové proxy (pouze pro authoring a kontrolu, nejdou do herního exportu)
TRUNK_C = Vector((0.060, -0.950, 0.0))
TRUNK_R = 0.15
CHOP_CONTACT = Vector((-0.030, -0.830, 0.720))
MINE_TARGET = Vector((-0.050, -0.720, -0.015))


def make_proxies():
    bark = mat('M_Proxy_Bark', (0.24, 0.17, 0.11), 0.9)
    soil = mat('M_Proxy_Ground', (0.25, 0.22, 0.18), 0.95)
    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=TRUNK_R, depth=2.2, location=(TRUNK_C.x, TRUNK_C.y, 1.1))
    t = bpy.context.active_object; t.name = 'PROXY_Trunk'; t.data.materials.append(bark)
    bpy.ops.mesh.primitive_plane_add(size=1.2, location=(MINE_TARGET.x, MINE_TARGET.y - 0.1, 0.0))
    g = bpy.context.active_object; g.name = 'PROXY_GroundPatch'; g.data.materials.append(soil)
    # značky kontaktu
    for nm, loc in (('PROXY_ChopContact', CHOP_CONTACT), ('PROXY_MineTarget', MINE_TARGET)):
        e = bpy.data.objects.new(nm, None); e.empty_display_type = 'SPHERE'; e.empty_display_size = 0.03
        e.location = loc; bpy.context.scene.collection.objects.link(e)
    for o in (t, g):
        o['proxy'] = True
    return t, g
