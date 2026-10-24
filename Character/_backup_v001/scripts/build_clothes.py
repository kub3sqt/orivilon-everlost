"""Etapa 3: oblečení defaultního cestovatele na schválené tělo a sdílenou kostru.
python3 build_clothes.py -- in_rig_anim.blend out_dressed.blend
"""
import sys, math, bpy, bmesh
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import clothes_lib as C, body_sdf as S
from clothes_lib import zig, mat
from mathutils import Vector, Matrix

src, dst = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.open_mainfile(filepath=src)
arm = bpy.data.objects['Traveler_Rig']
body = bpy.data.objects['Body']
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
col = bpy.data.collections.new('Outfit_Default'); bpy.context.scene.collection.children.link(col)

M = {k: mat('M_' + k, k) for k in C.PAL}


def A_s(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3 - 2 * t)

M['brass'] = mat('M_brass', 'brass', 0.35, 0.9)
M['linen'] = mat('M_linen', 'linen', 0.9, 0, 0.3)
M['scarf'] = mat('M_scarf', 'scarf', 0.9, 0, 0.4)


def to_col(ob):
    for c in ob.users_collection:
        c.objects.unlink(ob)
    col.objects.link(ob)
    return ob


def ang(c):
    return math.atan2(c.x, -c.y)  # 0 = vpředu, +pi/2 = levý bok


# ---------------------------------------------------------------- TUNIKA (horní část) – shell
def tunic_pred(c, ds, f):
    if c.z < 0.52 or c.z > 1.035:
        return False
    ok = {'spine', 'chest', 'hips', 'shoulder.L', 'shoulder.R', 'upper_arm.L', 'upper_arm.R', 'neck'}
    if not ds <= ok:
        return False
    for s in ('.L', '.R'):
        if 'upper_arm' + s in ds:
            t = C.bone_t(arm, 'upper_arm' + s, c)
            a = math.atan2(c.y, c.z)
            if t > 0.60 + zig(a * 1.0 + (0.6 if s == '.R' else 0), 5, 0.16) / 0.16 * 0.12:
                return False
    if c.z > 0.995 and c.y < -0.02:  # výstřih vpředu
        return False
    return True


def tunic_off(co, d):
    return 0.017 + 0.010 * max(0.0, 1.0 - abs(co.z - 0.80) / 0.2)


tunic = to_col(C.shell(body, arm, 'Outfit_Tunic', tunic_pred, tunic_off, M['linen']))
C.add_solidify(tunic, 0.006)

# ---------------------------------------------------------------- SUKNĚ TUNIKY – loft
def skirt():
    seg, rings = 56, 9
    z_top, base = 0.760, 0.430
    bm = bmesh.new(); grid = []
    for j in range(rings + 1):
        t = j / rings
        row = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            hem = base + zig(a, 13, 0.055, 0.3) + 0.02 * math.cos(a) ** 2  # vpředu/vzadu delší
            z = z_top + (hem - z_top) * t
            # horní část přiléhá k tělu (+ tunika), dolů se rozšiřuje do volné sukně
            r_fit = C.body_radius(a, z) + 0.024
            ra = 0.212 + 0.048 * t ** 1.3; rb = 0.172 + 0.033 * t ** 1.3
            r_ell = (ra * rb) / math.sqrt((rb * math.sin(a)) ** 2 + (ra * math.cos(a)) ** 2)
            k = A_s(t / 0.45)
            r = max(r_fit, r_fit * (1 - k) + r_ell * k)
            row.append(bm.verts.new((math.sin(a) * r, 0.012 - math.cos(a) * r, z)))
        grid.append(row)
    for j in range(rings):
        for i in range(seg):
            bm.faces.new((grid[j][i], grid[j][(i + 1) % seg], grid[j + 1][(i + 1) % seg], grid[j + 1][i]))
    me = bpy.data.meshes.new('Outfit_TunicSkirt'); bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new('Outfit_TunicSkirt', me); me.materials.append(M['linen'])
    C.link(ob, arm)
    near = C.nearest_weight_dicts(body, [v.co for v in me.vertices])

    def loft_w(co):
        h = min(max((0.66 - co.z) / 0.24, 0.0), 1.0)
        sl = min(max(co.x / 0.06, -1.0), 1.0)
        fl = 0.5 * (1 + sl); fr = 1 - fl
        wt = 0.55 * h
        return {'hips': 1 - wt, 'thigh.L': wt * fl, 'thigh.R': wt * fr}
    for v, nw in zip(me.vertices, near):
        k = A_s((z_top - v.co.z) / 0.16)  # nahoře váhy z těla (sleduje trup), dole volná sukně
        lw = loft_w(v.co)
        d = {}
        for n, w in nw.items():
            d[n] = d.get(n, 0) + w * (1 - k)
        for n, w in lw.items():
            d[n] = d.get(n, 0) + w * k
        sm = sum(d.values())
        for n, w in d.items():
            if w / sm > 0.005:
                (ob.vertex_groups.get(n) or ob.vertex_groups.new(name=n)).add([v.index], w / sm, 'REPLACE')
    C.add_solidify(ob, 0.006)
    return to_col(ob)


skirt_ob = skirt()

# ---------------------------------------------------------------- KALHOTY – shell + záplaty
def pants_pred(c, ds, f):
    if c.z > 0.72:
        return False
    if not ds <= {'hips', 'spine', 'thigh.L', 'thigh.R', 'shin.L', 'shin.R'}:
        return False
    return c.z > 0.150


def pants_off(co, d):
    knee = max(0.0, 1.0 - abs(co.z - 0.33) / 0.12)
    return 0.016 + 0.012 * knee + (0.006 if co.z > 0.5 else 0.0)


pants = to_col(C.shell(body, arm, 'Outfit_Pants', pants_pred, pants_off, M['pants']))
C.add_solidify(pants, 0.006)


def patch(name, center, radius, extra):
    def pr(c, ds, f):
        return (c - Vector(center)).length < radius and pants_pred(c, ds, f)
    p = C.shell(body, arm, name, pr, lambda co, d: pants_off(co, d) + extra, M['patch'], smooth_iters=1)
    C.add_solidify(p, 0.003)
    return to_col(p)


patch('Outfit_PantsPatch_KneeL', (0.12, -0.06, 0.33), 0.055, 0.004)
patch('Outfit_PantsPatch_ThighR', (-0.14, -0.06, 0.47), 0.045, 0.003)
patch('Outfit_PantsPatch_BackL', (0.11, 0.09, 0.40), 0.045, 0.004)

# ---------------------------------------------------------------- PŘEHOZ přes ramena (tmavý, potrhaný)
def mantle_pred(c, ds, f):
    if not ds <= {'chest', 'spine', 'shoulder.L', 'shoulder.R', 'upper_arm.L', 'upper_arm.R', 'neck'}:
        return False
    if c.z > 1.04:
        return False
    for s in ('.L', '.R'):
        if 'upper_arm' + s in ds and C.bone_t(arm, 'upper_arm' + s, c) > 0.30 + zig(math.atan2(c.y, c.z), 4, 0.1):
            return False
    a = ang(c)
    back = 0.5 * (1 - math.cos(a))  # 0 vpředu, 1 vzadu
    lim = 0.885 - 0.115 * back ** 1.5 - zig(a, 11, 0.06, 1.7)
    if c.z < lim:
        return False
    if c.y < -0.05 and abs(c.x) < 0.07 and c.z < 1.0:  # rozevření vpředu
        return False
    return True


mantle = to_col(C.shell(body, arm, 'Outfit_Mantle', mantle_pred, lambda co, d: 0.034 + 0.008 * (co.z < 0.95), M['mantle'], smooth_iters=5))
C.add_solidify(mantle, 0.007)

# ---------------------------------------------------------------- BOTY – shell + manžeta
def boot_pred(c, ds, f):
    return c.z < 0.205 and ds <= {'foot.L', 'foot.R', 'toe.L', 'toe.R', 'shin.L', 'shin.R'}


def boot_off(co, d):
    return 0.010 + 0.006 * (co.z > 0.06)


boots = to_col(C.shell(body, arm, 'Outfit_Boots', boot_pred, boot_off, M['boot'], smooth_iters=2))
for v in boots.data.vertices:  # rovná podrážka
    if v.co.z < 0.004:
        v.co.z = -0.002
C.add_solidify(boots, 0.007)


def ring_loft(name, center_fn, radius_fn, seg, rings, material, close=True):
    """Prstencový loft: center_fn(t)->(c, axis), radius_fn(t, a)->(r)."""
    bm = bmesh.new(); grid = []
    for j in range(rings + 1):
        t = j / rings
        c, ax, ref = center_fn(t)
        ax = ax.normalized(); u = (ref - ax * ref.dot(ax)).normalized(); v = ax.cross(u)
        row = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            r = radius_fn(t, a)
            row.append(bm.verts.new(c + (u * math.cos(a) + v * math.sin(a)) * r))
        grid.append(row)
    for j in range(rings):
        for i in range(seg):
            bm.faces.new((grid[j][i], grid[j][(i + 1) % seg], grid[j + 1][(i + 1) % seg], grid[j + 1][i]))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(name, me); me.materials.append(material)
    C.link(ob, arm)
    return ob


for s, sg in (('.L', 1), ('.R', -1)):
    an = Vector((S.P['ankle'][0] * sg, S.P['ankle'][1], S.P['ankle'][2]))
    kn = Vector((S.P['knee'][0] * sg, S.P['knee'][1], S.P['knee'][2]))
    ax = (kn - an).normalized()
    cuff = ring_loft('Outfit_BootCuff' + s, lambda t, an=an, ax=ax: (an + ax * (0.065 + 0.055 * t), ax, Vector((1, 0, 0))),
                     lambda t, a: 0.083 + 0.006 * math.sin(3 * a) * (t > 0.5) - 0.004 * t, 20, 3, M['boot'])
    C.nearest_weights(body, cuff); C.add_solidify(cuff, 0.008); to_col(cuff)

# ---------------------------------------------------------------- ZÁPĚSTNÍ PÁSKY
for s, sg in (('.L', 1), ('.R', -1)):
    el = Vector((S.P['elbow'][0] * sg, S.P['elbow'][1], S.P['elbow'][2]))
    wr = Vector((S.P['wrist'][0] * sg, S.P['wrist'][1], S.P['wrist'][2]))
    ax = (wr - el).normalized()
    band = ring_loft('Outfit_WristBand' + s, lambda t, el=el, ax=ax: (el + ax * (0.085 + 0.065 * t), ax, Vector((0, -1, 0))),
                     lambda t, a: 0.0625 - 0.004 * t + 0.003 * (abs(t - 0.5) < 0.2), 20, 4, M['leather'])
    C.nearest_weights(body, band); C.add_solidify(band, 0.007); to_col(band)

# ---------------------------------------------------------------- OPASEK + PŘEZKA
belt = ring_loft('Outfit_Belt', lambda t: (Vector((0, 0.012, 0.700 + 0.048 * t)), Vector((0, 0, 1)), Vector((0, -1, 0))),
                 lambda t, a: 0.2, 56, 2, M['leather'])
for v in belt.data.vertices:  # těsně přes horní lem sukně (sleduje tvar trupu)
    a = math.atan2(v.co.x, -(v.co.y - 0.012))
    r = C.body_radius(a, v.co.z) + 0.034
    v.co.x = r * math.sin(a); v.co.y = 0.012 - r * math.cos(a)
C.nearest_weights(body, belt); C.add_solidify(belt, 0.012, offset=1.0); to_col(belt)
bpy.ops.mesh.primitive_cube_add(size=1, location=(0.0, 0.012 - C.body_radius(0.0, 0.724) - 0.050, 0.724))
bk = bpy.context.active_object; bk.name = 'Outfit_BeltBuckle'; bk.scale = (0.062, 0.016, 0.054)
bpy.ops.object.transform_apply(scale=True)
bm = bmesh.new(); bm.from_mesh(bk.data)
bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.005, segments=2, affect='EDGES')
inner = [f for f in bm.faces if f.normal.y < -0.9]
r = bmesh.ops.inset_region(bm, faces=inner, thickness=0.011, depth=0.0)
bmesh.ops.translate(bm, vec=(0, 0.008, 0), verts=list({v for f in inner for v in f.verts}))
bm.to_mesh(bk.data); bm.free(); bk.data.materials.append(M['brass'])
C.link(bk, arm); C.nearest_weights(body, bk, k=8); to_col(bk)
for o in bpy.context.selected_objects:
    o.select_set(False)

# ---------------------------------------------------------------- ŠIKMÝ ŘEMEN + BRAŠNA
def surface_point(p, off):
    p = Vector(p)
    q = S.project(__import__('numpy').array([p[:]]), 8)[0]
    g = S.grad(__import__('numpy').array([q]))[0]
    n = Vector(g).normalized()
    return Vector(q) + n * off


strap_pts = [surface_point(p, o) for p, o in (
    ((0.07, 0.15, 0.80), 0.040), ((0.12, 0.12, 0.95), 0.050), ((0.13, 0.02, 1.01), 0.060),
    ((0.08, -0.12, 0.95), 0.050), ((0.00, -0.15, 0.86), 0.040), ((-0.10, -0.15, 0.77), 0.036),
    ((-0.17, -0.10, 0.70), 0.040))]


def ribbon(name, pts, width, thick, material, up_hint=None):
    bm = bmesh.new(); rows = []
    for i, p in enumerate(pts):
        tang = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        q = S.project(__import__('numpy').array([p[:]]), 8)[0]
        n = (p - Vector(q)).normalized() if up_hint is None else Vector(up_hint)
        side = tang.cross(n).normalized()
        n = side.cross(tang).normalized()
        row = [bm.verts.new(p + side * sx * width / 2 + n * nz * thick / 2) for sx, nz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        rows.append(row)
    for a, b in zip(rows[:-1], rows[1:]):
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bm.faces.new(list(reversed(rows[0]))); bm.faces.new(rows[-1])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); me.materials.append(material)
    C.link(ob, arm)
    sub = ob.modifiers.new('Sub', 'SUBSURF'); sub.levels = 1; sub.render_levels = 1
    bpy.context.view_layer.objects.active = ob
    while ob.modifiers.find('Sub') > 0:
        bpy.ops.object.modifier_move_up(modifier='Sub')
    return ob


def densify(pts, n=4):
    out = []
    for a, b in zip(pts[:-1], pts[1:]):
        for k in range(n):
            out.append(a.lerp(b, k / n))
    out.append(pts[-1])
    # vyhlazení
    for _ in range(3):
        out = [out[0]] + [(out[i - 1] + out[i] * 2 + out[i + 1]) / 4 for i in range(1, len(out) - 1)] + [out[-1]]
    return out


strap = ribbon('Outfit_Strap', densify(strap_pts), 0.034, 0.008, M['leather_dark'])
C.nearest_weights(body, strap); to_col(strap)

# brašna na pravém boku
bag_c = surface_point((-0.205, -0.06, 0.625), 0.080)
bpy.ops.mesh.primitive_cube_add(size=1, location=bag_c)
bag = bpy.context.active_object; bag.name = 'Outfit_Bag'
bag.scale = (0.055, 0.125, 0.11); bag.rotation_euler = (0, 0.12, math.radians(-28))
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
bv = bag.modifiers.new('Bevel', 'BEVEL'); bv.width = 0.014; bv.segments = 3
bpy.ops.object.modifier_apply(modifier='Bevel')
bag.data.materials.append(M['leather'])
for p in bag.data.polygons:
    p.use_smooth = True
# klopa
bpy.ops.mesh.primitive_cube_add(size=1, location=bag_c + Vector((-0.012, -0.004, 0.030)))
flap = bpy.context.active_object; flap.name = 'Outfit_BagFlap'
flap.scale = (0.062, 0.132, 0.052); flap.rotation_euler = (0, 0.12, math.radians(-28))
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
bv = flap.modifiers.new('Bevel', 'BEVEL'); bv.width = 0.01; bv.segments = 2
bpy.ops.object.modifier_apply(modifier='Bevel')
flap.data.materials.append(M['leather_dark'])
bpy.ops.object.select_all(action='DESELECT')
for o in (bag, flap):
    o.select_set(True)
bpy.context.view_layer.objects.active = bag
bpy.ops.object.join()
bag = bpy.context.active_object
C.link(bag, arm)
C.set_weights(bag, lambda co: {'hips': 0.8, 'thigh.R': 0.2}); to_col(bag)

# ---------------------------------------------------------------- ŠÁTEK
neck_c = Vector((0, 0.012, 1.0))


def scarf_center(t):
    a = 2 * math.pi * t
    return (Vector((0.0, 0.010, 1.045)), Vector((0, 0, 1)), Vector((0, -1, 0)))


def scarf_wrap():
    seg, rings = 40, 8
    bm = bmesh.new(); grid = []
    for j in range(rings):
        phi = 2 * math.pi * j / rings
        row = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            R = 0.110 + 0.008 * math.sin(3 * a)
            # průřez: zploštělá smyčka (výška 0.075, tloušťka 0.030), dva navinuté pruhy
            h = 0.054 * math.sin(phi) + 0.004 * math.sin(5 * a + j)
            d = 0.038 * math.cos(phi) + 0.006 * math.sin(2 * a)
            zc = 1.040 - 0.020 * math.cos(a)  # vpředu níž (cípy dolů)
            p = Vector((math.sin(a) * (R + d), 0.010 - math.cos(a) * (R + d), zc + h))
            row.append(bm.verts.new(p))
        grid.append(row)
    for j in range(rings):
        for i in range(seg):
            bm.faces.new((grid[j][i], grid[j][(i + 1) % seg], grid[(j + 1) % rings][(i + 1) % seg], grid[(j + 1) % rings][i]))
    me = bpy.data.meshes.new('Outfit_ScarfWrap'); bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new('Outfit_ScarfWrap', me); me.materials.append(M['scarf'])
    C.link(ob, arm)
    C.set_weights(ob, lambda co: {'neck': 0.55 + 0.4 * min(max((co.z - 1.02) / 0.06, 0), 1), 'chest': 0.45 - 0.4 * min(max((co.z - 1.02) / 0.06, 0), 1)})
    return to_col(ob)


scarf_wrap()


def scarf_tail(name, pts, w0, w1, material):
    bm = bmesh.new(); rows = []
    n = len(pts)
    for i, p in enumerate(pts):
        t = i / (n - 1)
        tang = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        q = S.project(__import__('numpy').array([p[:]]), 8)[0]
        nrm = (p - Vector(q)).normalized()
        side = tang.cross(nrm).normalized()
        w = w0 + (w1 - w0) * t
        k = 5
        row = []
        for j in range(k):
            s = (j / (k - 1) - 0.5) * w
            wave = 0.006 * math.sin(j * 2.2 + i * 0.9)  # záhyby látky
            row.append(bm.verts.new(p + side * s + nrm * wave))
        rows.append(row)
    for a, b in zip(rows[:-1], rows[1:]):
        for j in range(len(a) - 1):
            bm.faces.new((a[j], a[j + 1], b[j + 1], b[j]))
    # roztřepený konec
    last = rows[-1]
    for j, v in enumerate(last):
        v.co.z -= 0.025 * (j % 2)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(name, me); me.materials.append(material)
    C.link(ob, arm); C.nearest_weights(body, ob, k=6)
    C.add_solidify(ob, 0.008, offset=0.0)
    return to_col(ob)


knot = surface_point((-0.06, -0.10, 1.00), 0.050)
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=0.044, location=knot)
kn = bpy.context.active_object; kn.name = 'Outfit_ScarfKnot'; kn.scale = (1.15, 0.8, 0.9)
bpy.ops.object.transform_apply(scale=True)
for p in kn.data.polygons:
    p.use_smooth = True
kn.data.materials.append(M['scarf_dark']); C.link(kn, arm)
C.set_weights(kn, lambda co: {'neck': 0.4, 'chest': 0.6}); to_col(kn)
tail1 = [surface_point(p, o) for p, o in (((-0.07, -0.13, 0.99), 0.05), ((-0.09, -0.15, 0.93), 0.05),
                                           ((-0.10, -0.16, 0.86), 0.052), ((-0.11, -0.16, 0.79), 0.055), ((-0.115, -0.15, 0.74), 0.058))]
scarf_tail('Outfit_ScarfTailFront', densify(tail1, 3), 0.090, 0.060, M['scarf'])
tail2 = [surface_point(p, o) for p, o in (((-0.04, -0.12, 0.99), 0.052), ((-0.02, -0.15, 0.94), 0.056),
                                           ((-0.01, -0.165, 0.89), 0.06))]
scarf_tail('Outfit_ScarfTailFront2', densify(tail2, 3), 0.075, 0.05, M['scarf_dark'])

# ---------------------------------------------------------------- KLOBOUK (tuhý, kost head)
def hat():
    hc = Vector(S.P['head_c'])
    base_z = hc.z + 0.072
    seg = 48
    bm = bmesh.new()
    # koruna (lathe) – mírně kónická, promáčklá, pootočená
    prof = [(0.000, 0.208), (0.040, 0.200), (0.090, 0.184), (0.140, 0.160), (0.185, 0.128), (0.220, 0.090), (0.240, 0.052), (0.250, 0.020)]
    rows = []
    for (h, r) in prof:
        row = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            rr = r * (1 + 0.035 * math.sin(3 * a + 0.7) + 0.02 * math.sin(5 * a))
            dent = -0.045 * max(0.0, math.cos(a - 0.4)) * (h / 0.25) ** 2  # promáčknutí vpředu nahoře
            lean = 0.030 * (h / 0.25) ** 1.5  # špička koruny spadá dozadu
            row.append(bm.verts.new((math.sin(a) * rr, -math.cos(a) * rr + lean, base_z + h + dent)))
        rows.append(row)
    for a, b in zip(rows[:-1], rows[1:]):
        for i in range(seg):
            bm.faces.new((a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]))
    top = bm.verts.new((sum(v.co.x for v in rows[-1]) / seg, sum(v.co.y for v in rows[-1]) / seg, sum(v.co.z for v in rows[-1]) / seg + 0.006))
    for i in range(seg):
        bm.faces.new((rows[-1][i], rows[-1][(i + 1) % seg], top))
    # krempa – nepravidelný okraj, mírné spadnutí do stran
    brim_rows = [rows[0]]
    for k, (t) in enumerate((0.33, 0.66, 1.0)):
        row = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            outer = 0.338 + 0.014 * math.sin(3 * a + 1.3) + 0.008 * math.sin(7 * a) - zig(a, 19, 0.020, 0.9)
            r = 0.208 + (outer - 0.208) * t
            droop = -0.060 * t ** 1.6 * (0.5 + 0.5 * math.sin(a) ** 2) + 0.016 * t * math.cos(a) + 0.012 * t ** 2 * math.sin(2 * a + 0.8)
            row.append(bm.verts.new((math.sin(a) * r, -math.cos(a) * r + 0.004, base_z + droop)))
        brim_rows.append(row)
    for a, b in zip(brim_rows[:-1], brim_rows[1:]):
        for i in range(seg):
            bm.faces.new((a[i], b[i], b[(i + 1) % seg], a[(i + 1) % seg]))
    me = bpy.data.meshes.new('Outfit_Hat'); bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new('Outfit_Hat', me); me.materials.append(M['hat'])
    C.link(ob, arm); C.set_weights(ob, lambda co: {'head': 1.0})
    C.add_solidify(ob, 0.010, offset=-1.0)
    to_col(ob)
    # páska
    band = ring_loft('Outfit_HatBand', lambda t: (Vector((0, 0.002, base_z + 0.012 + 0.040 * t)), Vector((0, 0, 1)), Vector((0, -1, 0))),
                     lambda t, a: (0.209 - 0.012 * t) * (1 + 0.035 * math.sin(3 * a + 0.7) + 0.02 * math.sin(5 * a)) + 0.004,
                     48, 2, M['hat_band'])
    C.set_weights(band, lambda co: {'head': 1.0}); C.add_solidify(band, 0.004, offset=1.0); to_col(band)
    # záplaty na koruně (2 ks, pevné)
    def crown_r(h):
        for (h0, r0), (h1, r1) in zip(prof[:-1], prof[1:]):
            if h0 <= h <= h1:
                return r0 + (r1 - r0) * (h - h0) / (h1 - h0)
        return prof[-1][1]
    for nm, a0, h0 in (('Outfit_HatPatch1', 0.9, 0.12),):
        bm = bmesh.new(); g = []
        for jj in range(3):
            rr = []
            for ii in range(3):
                a = a0 + (ii - 1) * 0.17
                h = h0 + (jj - 1) * 0.03
                r = crown_r(h) * (1 + 0.035 * math.sin(3 * a + 0.7) + 0.02 * math.sin(5 * a)) + 0.004
                dent = -0.045 * max(0.0, math.cos(a - 0.4)) * (h / 0.25) ** 2
                lean = 0.030 * (h / 0.25) ** 1.5
                rr.append(bm.verts.new((math.sin(a) * r, -math.cos(a) * r + lean, base_z + h + dent)))
            g.append(rr)
        for jj in range(2):
            for ii in range(2):
                bm.faces.new((g[jj][ii], g[jj][ii + 1], g[jj + 1][ii + 1], g[jj + 1][ii]))
        me = bpy.data.meshes.new(nm); bm.to_mesh(me); bm.free()
        po = bpy.data.objects.new(nm, me); me.materials.append(M['hat_patch'])
        C.link(po, arm); C.set_weights(po, lambda co: {'head': 1.0}); C.add_solidify(po, 0.003, offset=1.0); to_col(po)
    return ob


hat()

# ---------------------------------------------------------------- skrytí těla pod oděvem (nedestruktivně)
dom = C.dominant_groups(body)
hide = body.vertex_groups.new(name='HIDE_under_outfit')
idx = []
for v, d in zip(body.data.vertices, dom):
    c = v.co
    if d in ('spine', 'chest', 'hips') and 0.64 < c.z < 0.93:
        idx.append(v.index)
    elif d.startswith('upper_arm') and C.bone_t(arm, d, c) < 0.40:
        idx.append(v.index)
    elif d.startswith(('thigh', 'shin')) and 0.24 < c.z < 0.60:
        idx.append(v.index)
    elif d.startswith(('foot', 'toe')) and c.z < 0.10:
        idx.append(v.index)
hide.add(idx, 1.0, 'REPLACE')
mk = body.modifiers.new('HideUnderOutfit', 'MASK'); mk.vertex_group = 'HIDE_under_outfit'; mk.invert_vertex_group = True
bpy.context.view_layer.objects.active = body
while body.modifiers.find('HideUnderOutfit') > 0:
    bpy.ops.object.modifier_move_up(modifier='HideUnderOutfit')

for o in col.objects:
    o['outfit'] = 'DefaultTraveler'
print('outfit parts', len(col.objects), [o.name for o in col.objects])
bpy.ops.wm.save_as_mainfile(filepath=dst)
