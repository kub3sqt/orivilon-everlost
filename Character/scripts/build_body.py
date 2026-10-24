"""Etapa 1: stavba deformovatelné quad sítě bílého těla.

Postup (retopologie projekcí):
 1. Kostra vrcholů s poloměry -> Skin modifier = čistá quad klec s kruhovými smyčkami
    podél končetin (smyčky navíc v loktech, kolenou, ramenou a kyčlích).
 2. Catmull-Clark subdivision (2 úrovně) -> hustota pro hladkou siluetu.
 3. Opakovaně: tangenciální relaxace + projekce na analytickou SDF plochu (body_sdf.py).
    Výsledkem je tvar ze SDF a toky hran z klece.
 4. Symetrie: levá polovina se zrcadlí, aby síť byla přesně symetrická.
"""
import sys, numpy as np, bpy, bmesh
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import body_sdf as S
from mathutils import Vector

P = S.P


def lerp(a, b, t):
    return a + (b - a) * t


def build_cage():
    verts, radii, edges = [], [], []

    def add(p, r, parent=None):
        verts.append(Vector(p)); radii.append(r)
        i = len(verts) - 1
        if parent is not None:
            edges.append((parent, i))
        return i

    root = add((0, 0.010, 0.600), (0.13, 0.10))
    pel = add((0, 0.010, 0.680), (0.15, 0.11), root)
    bel = add((0, -0.004, 0.770), (0.16, 0.12), pel)
    che = add((0, 0.004, 0.880), (0.17, 0.12), bel)
    up = add((0, 0.006, 0.960), (0.14, 0.10), che)
    nb = add((0, 0.008, 1.040), (0.07, 0.07), up)
    nt = add((0, 0.006, 1.100), (0.075, 0.075), nb)
    hc = P['head_c']
    h0 = add((0, 0.004, 1.150), (0.16, 0.15), nt)
    h1 = add((0, 0.0, hc[2] - 0.06), (0.20, 0.19), h0)
    h2 = add((0, 0.0, hc[2] + 0.05), (0.20, 0.19), h1)
    h3 = add((0, 0.0, hc[2] + 0.15), (0.14, 0.13), h2)
    h4 = add((0, 0.0, hc[2] + 0.205), (0.06, 0.06), h3)

    for side in (1, -1):
        m = np.array([side, 1, 1])
        sh, el, wr = P['shoulder'] * m, P['elbow'] * m, P['wrist'] * m
        cl = add(tuple(lerp(np.array([0, 0.006, 0.960]), sh, 0.55)), (0.07, 0.07), up)
        a0 = add(tuple(sh), (0.068, 0.068), cl)
        a1 = add(tuple(lerp(sh, el, 0.40)), (0.062, 0.062), a0)
        a2 = add(tuple(lerp(sh, el, 0.75)), (0.058, 0.058), a1)
        a3 = add(tuple(el), (0.056, 0.056), a2)
        a4 = add(tuple(lerp(el, wr, 0.30)), (0.055, 0.055), a3)
        a5 = add(tuple(lerp(el, wr, 0.70)), (0.052, 0.052), a4)
        a6 = add(tuple(wr), (0.050, 0.050), a5)
        f, s_, u = S.rot_frame(wr - el, np.array([0, -1.0, 0]))
        h_1 = add(tuple(wr + f * 0.060), (0.075, 0.06), a6)
        h_2 = add(tuple(wr + f * 0.120), (0.07, 0.06), h_1)
        h_3 = add(tuple(wr + f * 0.170), (0.04, 0.04), h_2)
        # palec: lokální (podél f, šířka u, tloušťka s)
        t0 = add(tuple(wr + f * 0.045 + u * 0.060), (0.03, 0.03), h_1)
        t1 = add(tuple(wr + f * 0.092 + u * 0.085 + s_ * 0.014), (0.026, 0.026), t0)

        hp, kn, an = P['hip'] * m, P['knee'] * m, P['ankle'] * m
        l0 = add(tuple(hp + np.array([0, 0, -0.03])), (0.10, 0.10), root)
        l1 = add(tuple(lerp(hp, kn, 0.45)), (0.09, 0.09), l0)
        l2 = add(tuple(lerp(hp, kn, 0.82)), (0.08, 0.08), l1)
        l3 = add(tuple(kn), (0.075, 0.075), l2)
        l4 = add(tuple(lerp(kn, an, 0.20)), (0.072, 0.072), l3)
        l5 = add(tuple(lerp(kn, an, 0.60)), (0.065, 0.065), l4)
        l6 = add(tuple(an + np.array([0, 0, 0.02])), (0.058, 0.058), l5)
        f1 = add(tuple(an + np.array([0, -0.040, -0.045])), (0.055, 0.045), l6)
        f2 = add(tuple(an + np.array([0, -0.115, -0.050])), (0.045, 0.04), f1)
    return verts, radii, edges, root


def make_body(name='Body'):
    verts, radii, edges, root = build_cage()
    me = bpy.data.meshes.new(name + '_cage')
    me.from_pydata([tuple(v) for v in verts], edges, [])
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    sk = ob.modifiers.new('Skin', 'SKIN')
    sk.use_smooth_shade = True
    sk.branch_smoothing = 0.6
    for i, sv in enumerate(me.skin_vertices[''].data):
        sv.radius = radii[i]
        sv.use_root = (i == root)
    sub = ob.modifiers.new('Sub', 'SUBSURF'); sub.levels = 2; sub.render_levels = 2
    dg = bpy.context.evaluated_depsgraph_get()
    me2 = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.clear()
    old = ob.data; ob.data = me2; me2.name = name; bpy.data.meshes.remove(old)
    return ob


def densify_head(ob, z_t=1.05):
    """Hlava je hlavní čitelný tvar: zdvojnásob hustotu nad krkem (16 -> 32 segmentů).
    Přechodová řada trojúhelníků leží pod bradou na krku (krytá šátkem, tuhá oblast)."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    edges = [e for e in bm.edges if all(v.co.z > z_t for v in e.verts)]
    bmesh.ops.subdivide_edges(bm, edges=edges, cuts=1, use_grid_fill=True, quad_corner_type='INNER_VERT')
    ng = [f for f in bm.faces if len(f.verts) > 4]
    if ng:
        bmesh.ops.triangulate(bm, faces=ng, quad_method='BEAUTY', ngon_method='BEAUTY')
    # sloučit dvojice trojúhelníků zpět do quadů, kde to jde
    tris = [f for f in bm.faces if len(f.verts) == 3]
    bmesh.ops.join_triangles(bm, faces=tris, angle_face_threshold=0.6, angle_shape_threshold=0.9)
    bm.to_mesh(ob.data); bm.free(); ob.data.update()


def neighbors(me):
    n = len(me.vertices)
    nb = [[] for _ in range(n)]
    for e in me.edges:
        a, b = e.vertices
        nb[a].append(b); nb[b].append(a)
    return nb


def relax_project(ob, iters=25, lam=0.5):
    me = ob.data
    co = np.array([v.co[:] for v in me.vertices])
    nb = neighbors(me)
    idx = np.repeat(np.arange(len(nb)), [len(x) for x in nb])
    flat = np.concatenate([np.array(x, dtype=np.int64) for x in nb])
    cnt = np.maximum(np.array([len(x) for x in nb]), 1)[:, None]
    co = S.project(co, 8)
    for it in range(iters):
        avg = np.zeros_like(co)
        np.add.at(avg, idx, co[flat]); avg /= cnt
        g = S.grad(co); g /= np.linalg.norm(g, axis=1, keepdims=True)
        d = avg - co
        d -= (np.sum(d * g, 1))[:, None] * g  # jen tangenciálně
        co = co + lam * d
        co = S.project(co, 4)
    for v, c in zip(me.vertices, co):
        v.co = c
    me.update()
    return np.abs(S.body_sdf(co)).max()


def symmetrize(ob):
    me = ob.data
    co = np.array([v.co[:] for v in me.vertices])
    from scipy.spatial import cKDTree
    t = cKDTree(co)
    mir = co * np.array([-1, 1, 1])
    d, j = t.query(mir)
    bad = (d > 0.01).sum()
    for i, v in enumerate(me.vertices):
        if co[i, 0] > 1e-5:
            pass
        elif co[i, 0] < -1e-5:
            v.co = Vector(mir[j[i]] * np.array([1, 1, 1]))
            v.co.x = -co[j[i], 0]
            v.co.y = co[j[i], 1]; v.co.z = co[j[i], 2]
        else:
            v.co.x = 0.0
    me.update()
    return bad


if __name__ == '__main__':
    import render_util as RU
    RU.reset()
    ob = make_body()
    densify_head(ob)
    err = relax_project(ob)
    print('verts', len(ob.data.vertices), 'faces', len(ob.data.polygons), 'max sdf err', err)
    for p in ob.data.polygons:
        p.use_smooth = True
    # UV (jednoduchá smart projekce – tělo je jednobarevné, UV kvůli exportu/budoucím texturám)
    bpy.context.view_layer.objects.active = ob; ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')
    ob.data.materials.append(RU.ivory_material('M_Body_Ivory'))
    ob['everlost_note'] = 'Bílé tělo, A-pose, Z-up, čelem -Y, metry. Zdroj tvaru: scripts/body_sdf.py'
    out = sys.argv[-1] if sys.argv[-1].endswith('.blend') else '/home/claude/char/work/body_topo_test.blend'
    bpy.ops.wm.save_as_mainfile(filepath=out)
