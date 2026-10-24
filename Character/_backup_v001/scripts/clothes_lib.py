"""Etapa 3: pevný defaultní skin cestovatele (bez generátoru / random seedů).

Oděvy jsou samostatné meshe sdílející kostru Traveler_Rig:
 * shell díly (tunika-horní část, přehoz, kalhoty, boty, zápěstní pásky) vznikají z ploch
   schváleného těla posunutých po normálách -> váhy se kopírují 1:1 z těla (stejná deformace),
 * sukně tuniky je samostatný loft kolem boků s vlastními váhami (boky + částečně stehna),
 * klobouk je tuhý (100 % head), šátek/řemen/opasek/brašna převezmou váhy z nejbližšího těla.
Rozedrané lemy jsou deterministické (pevné „zuby“ z funkcí úhlu), žádná náhoda.
"""
import math, bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree

# ----------------------------------------------------------------- materiály (paleta z reference)
PAL = {
    'linen': (0.62, 0.45, 0.27), 'linen_dark': (0.45, 0.31, 0.18),
    'mantle': (0.13, 0.105, 0.09), 'scarf': (0.55, 0.13, 0.05), 'scarf_dark': (0.40, 0.09, 0.04),
    'pants': (0.115, 0.08, 0.06), 'patch': (0.30, 0.18, 0.10),
    'hat': (0.33, 0.19, 0.10), 'hat_band': (0.10, 0.075, 0.06), 'hat_patch': (0.25, 0.15, 0.08),
    'leather': (0.27, 0.15, 0.08), 'leather_dark': (0.17, 0.10, 0.06), 'brass': (0.62, 0.42, 0.16),
    'boot': (0.30, 0.17, 0.09), 'sole': (0.12, 0.08, 0.06),
}


def mat(name, key, rough=0.85, metal=0.0, sheen=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    c = PAL[key]
    b.inputs['Base Color'].default_value = (*c, 1)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    try:
        b.inputs['Sheen Weight'].default_value = sheen
    except Exception:
        pass
    m.diffuse_color = (*c, 1)
    return m


# ----------------------------------------------------------------- pomocné
def dominant_groups(body):
    names = {g.index: g.name for g in body.vertex_groups}
    dom = []
    for v in body.data.vertices:
        if v.groups:
            g = max(v.groups, key=lambda x: x.weight); dom.append(names[g.group])
        else:
            dom.append('')
    return dom


def bone_t(arm, bone, co):
    """Parametr podél kosti (0 = hlava, 1 = ocas) pro bod v rest pozici."""
    b = arm.data.bones[bone]
    h, t = b.head_local, b.tail_local
    d = t - h
    return (Vector(co) - h).dot(d) / d.length_squared


def link(ob, arm, coll=None):
    if not ob.users_collection:
        (coll or bpy.context.scene.collection).objects.link(ob)
    ob.parent = arm
    m = ob.modifiers.new('Armature', 'ARMATURE'); m.object = arm
    return ob


def add_solidify(ob, thick, offset=-1.0, rim=True):
    s = ob.modifiers.new('Solidify', 'SOLIDIFY')
    s.thickness = thick; s.offset = offset; s.use_rim = rim; s.use_even_offset = True
    s.use_quality_normals = True
    # solidify musí být PŘED armature
    bpy.context.view_layer.objects.active = ob
    while ob.modifiers.find('Solidify') > 0:
        bpy.ops.object.modifier_move_up(modifier='Solidify')
    return s


def copy_weights(src_body, dst, vmap):
    """vmap[i_dst] = i_src – kopie vah 1:1."""
    for g in src_body.vertex_groups:
        if dst.vertex_groups.get(g.name) is None:
            dst.vertex_groups.new(name=g.name)
    src = src_body.data.vertices
    for i_dst, i_src in enumerate(vmap):
        for gg in src[i_src].groups:
            name = src_body.vertex_groups[gg.group].name
            dst.vertex_groups[name].add([i_dst], gg.weight, 'REPLACE')


def nearest_weights(src_body, dst, k=4, smooth_rad=0.03):
    """Váhy z nejbližších vrcholů těla (vážený průměr k nejbližších)."""
    me = src_body.data
    kd = KDTree(len(me.vertices))
    for v in me.vertices:
        kd.insert(v.co, v.index)
    kd.balance()
    for g in src_body.vertex_groups:
        if dst.vertex_groups.get(g.name) is None:
            dst.vertex_groups.new(name=g.name)
    names = [g.name for g in src_body.vertex_groups]
    for v in dst.data.vertices:
        acc = {}
        tot = 0.0
        for (co, idx, dist) in kd.find_n(v.co, k):
            w = 1.0 / max(dist, 0.004)
            tot += w
            for gg in me.vertices[idx].groups:
                acc[names[gg.group]] = acc.get(names[gg.group], 0.0) + gg.weight * w
        s = sum(acc.values()) or 1.0
        for n, w in acc.items():
            if w / s > 0.01:
                dst.vertex_groups[n].add([v.index], w / s, 'REPLACE')


def set_weights(ob, fn):
    """fn(co) -> {bone: weight}"""
    for v in ob.data.vertices:
        d = fn(v.co)
        s = sum(d.values()) or 1.0
        for n, w in d.items():
            g = ob.vertex_groups.get(n) or ob.vertex_groups.new(name=n)
            g.add([v.index], w / s, 'REPLACE')


def shell(body, arm, name, face_pred, offset_fn, material, smooth_iters=4, cut_pred=None, boundary_iters=6):
    """Kopie vybraných ploch těla posunutá po normálách. face_pred(center, dom_set, verts)->bool."""
    me = body.data
    dom = dominant_groups(body)
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table(); bm.normal_update()
    keep = []
    for f in bm.faces:
        c = f.calc_center_median()
        if face_pred(c, set(dom[v.index] for v in f.verts), f):
            if cut_pred is None or not cut_pred(c, set(dom[v.index] for v in f.verts)):
                keep.append(f)
    vids = sorted(set(v.index for f in keep for v in f.verts))
    remap = {o: i for i, o in enumerate(vids)}
    nbm = bmesh.new()
    nv = []
    for o in vids:
        v = bm.verts[o]
        nv.append(nbm.verts.new(v.co + v.normal * offset_fn(v.co, dom[o])))
    for f in keep:
        nbm.faces.new([nv[remap[v.index]] for v in f.verts])
    nbm.verts.ensure_lookup_table()
    # tangenciální vyhlazení posunuté plochy (zabrání sevření v podpaží/rozkroku)
    for _ in range(smooth_iters):
        new = []
        for v in nbm.verts:
            if v.is_boundary or not v.link_edges:
                new.append(v.co.copy()); continue
            avg = sum((e.other_vert(v).co for e in v.link_edges), Vector()) / len(v.link_edges)
            new.append(v.co.lerp(avg, 0.35))
        for v, c in zip(nbm.verts, new):
            v.co = c
    # vyhlazení schodovitých okrajů (lem po plochách) – relaxace hraniční smyčky
    for _ in range(boundary_iters):
        new = {}
        for v in nbm.verts:
            if not v.is_boundary:
                continue
            nb = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(nb) == 2:
                new[v] = v.co.lerp((nb[0].co + nb[1].co) * 0.5, 0.5)
        for v, c in new.items():
            v.co = c
    out = bpy.data.meshes.new(name)
    nbm.to_mesh(out); nbm.free(); bm.free()
    ob = bpy.data.objects.new(name, out)
    for p in out.polygons:
        p.use_smooth = True
    out.materials.append(material)
    link(ob, arm)
    copy_weights(body, ob, vids)
    return ob


def zig(angle, teeth, amp, phase=0.0, irregular=0.35):
    """Deterministický „roztřepený“ lem: pilové zuby s pevnou nepravidelností (bez random)."""
    x = angle * teeth / (2 * math.pi) + phase
    fr = x - math.floor(x)
    tri = 1.0 - abs(2 * fr - 1.0)
    k = math.floor(x)
    var = 0.5 + 0.5 * math.sin(k * 2.399 + phase * 3.1)  # zlatý úhel – pevná variace zubů
    return amp * tri * (1.0 - irregular + irregular * var)


def body_radius(a, z, cy=0.012, lo=0.03, hi=0.34):
    """Poloměr povrchu těla (SDF) ve výšce z ve směru úhlu a (0 = vpředu, +pi/2 = levý bok)."""
    import body_sdf as S
    import numpy as np
    def f(r):
        return S.body_sdf(np.array([[math.sin(a) * r, cy - math.cos(a) * r, z]]))[0]
    # první průsečík zevnitř ven (ne paže/ruce dál od trupu)
    r = lo
    while r < hi and f(r) < 0:
        r += 0.004
    lo, hi = r - 0.004, r
    return 0.5 * (lo + hi)


def nearest_weight_dicts(src_body, points, k=4):
    me = src_body.data
    kd = KDTree(len(me.vertices))
    for v in me.vertices:
        kd.insert(v.co, v.index)
    kd.balance()
    names = [g.name for g in src_body.vertex_groups]
    out = []
    for p in points:
        acc = {}
        for (co, idx, dist) in kd.find_n(p, k):
            w = 1.0 / max(dist, 0.004)
            for gg in me.vertices[idx].groups:
                acc[names[gg.group]] = acc.get(names[gg.group], 0.0) + gg.weight * w
        sm = sum(acc.values()) or 1.0
        out.append({n: w / sm for n, w in acc.items() if w / sm > 0.01})
    return out
