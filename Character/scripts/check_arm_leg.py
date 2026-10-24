"""v004: průnik paží (předloktí, ruka, prsty, palec – posed mesh těla) do skutečné nohy VČETNĚ kalhot.
Noha = oblast kosti thigh/shin; povrch kalhot (vnější stěna) se v rest pózi tabeluje v lokálním
rámci kosti (t podél kosti × úhel) a v póze se body paže převedou do stejného rámce.
python3 check_arm_leg.py -- dressed.blend Akce [sub=4] [margin_mm=0]"""
import sys, math, bpy, numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0]); an = args[1]
sub = int(next((a.split('=')[1] for a in args if a.startswith('sub=')), 4))
ob = bpy.data.objects['Traveler_Rig']; P = ob.pose.bones
body = bpy.data.objects['Body']; pants = bpy.data.objects.get('Outfit_Pants')
PAD = 0.0 if pants else 0.024  # bez oblečení: kalhoty ~ tělo + 24 mm
for m in body.modifiers:
    if m.type == 'MASK': m.show_viewport = False
names = {g.index: g.name for g in body.vertex_groups}
def dom_of(o):
    nm = {g.index: g.name for g in o.vertex_groups}
    out = []
    for v in o.data.vertices:
        gs = [g for g in v.groups if nm[g.group] in P]
        out.append(nm[max(gs, key=lambda g: g.weight).group] if gs else '')
    return out
bd = dom_of(body)
ARM = [i for i, d in enumerate(bd) if d.split('.')[0] in ('forearm', 'forearm_twist', 'hand', 'fingers', 'thumb')]
armside = ['.L' if bd[i].endswith('.L') else '.R' for i in ARM]
LEGB = [b + s for b in ('thigh', 'shin') for s in ('.L', '.R')]
NT, NA = 24, 36
# rest tabulka poloměru vnějšího povrchu: max(kalhoty, tělo)
ob.animation_data.action = None
for pb in P: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
bpy.context.view_layer.update(); dg = bpy.context.evaluated_depsgraph_get()
R = {b: np.zeros((NT, NA)) for b in LEGB}
def local(bn, p):
    b = ob.data.bones[bn]; M = b.matrix_local; q = M.inverted() @ p  # Y podél kosti
    t = q.y / b.length; a = math.atan2(q.z, q.x); r = math.hypot(q.x, q.z)
    return t, a, r
for o in ([pants, body] if pants else [body]):
    oe = o.evaluated_get(dg); me = oe.to_mesh()
    d = dom_of(o) if o is body else None
    # evaluovaný mesh kalhot (solidify) má víc vrcholů -> dominanta podle nejbližšího originálu
    if o is pants:
        co0 = np.array([v.co[:] for v in o.data.vertices]); dp = dom_of(o)
    for i, v in enumerate(me.vertices):
        if o is pants:
            j = int(np.argmin(((co0 - np.array(v.co[:])) ** 2).sum(1))); bn = dp[j]
        else:
            bn = d[i]
        if bn not in R: continue
        t, a, r = local(bn, v.co)
        if -0.05 <= t <= 1.05:
            ti = min(max(int((t + 0.05) / 1.1 * NT), 0), NT - 1); ai = int((a + math.pi) / (2 * math.pi) * NA) % NA
            R[bn][ti, ai] = max(R[bn][ti, ai], r + (PAD if o is body else 0.0))
    oe.to_mesh_clear()
for b in R:  # vyplnit díry tabulky
    T = R[b]
    for _ in range(3):
        Z = T == 0
        if not Z.any(): break
        T[Z] = np.maximum.reduce([np.roll(T, 1, 1), np.roll(T, -1, 1), np.roll(T, 1, 0), np.roll(T, -1, 0)])[Z]
PTS = next((a.split('=')[1] for a in args if a.startswith('pts=')), None)
if PTS:  # místo paží testuj vrcholy daného dílu (např. lem tuniky proti stehnům)
    pob = bpy.data.objects[PTS]
ob.animation_data.action = bpy.data.actions[an]; act = ob.animation_data.action
sc = bpy.context.scene; worst = {}; per = []; HITZ = []
for f in range(int(act.frame_start), int(act.frame_end)):
    for k in range(sub):
        sc.frame_set(f, subframe=k / sub); dg = bpy.context.evaluated_depsgraph_get()
        if PTS:
            pe = pob.evaluated_get(dg); me = pe.to_mesh(); co = [pe.matrix_world @ v.co for v in me.vertices]; pe.to_mesh_clear()
            armside = ['.L' if c.x > 0 else '.R' for c in co]
        else:
            be = body.evaluated_get(dg); me = be.to_mesh(); co = [me.vertices[i].co.copy() for i in ARM]; be.to_mesh_clear()
        fr = {}
        for bn in LEGB:
            Mi = (P[bn].matrix).inverted(); L = ob.data.bones[bn].length
            for p, s in zip(co, armside):
                q = Mi @ p; t = q.y / L
                if t < 0.02 or t > 0.98: continue
                a = math.atan2(q.z, q.x); r = math.hypot(q.x, q.z)
                ti = min(max(int((t + 0.05) / 1.1 * NT), 0), NT - 1); ai = int((a + math.pi) / (2 * math.pi) * NA) % NA
                pen = R[bn][ti, ai] - r
                key = s + '->' + bn
                if PTS and pen > 0.002: HITZ.append(round(t, 2))
                if pen > fr.get(key, 0): fr[key] = pen
        tt = round(f + k / sub, 2)
        for key, v in fr.items():
            if v > worst.get(key, (0, 0))[0]: worst[key] = (v, tt)
        per.append((tt, round(max(fr.values()) * 1000, 1) if fr else 0.0))
print('ARMLEG', an, {k: (round(v[0] * 1000, 1), v[1]) for k, v in sorted(worst.items(), key=lambda x: -x[1][0])})
bad = [p for p in per if p[1] > 0]
import collections
if PTS: print('HIT_T_HIST', sorted(collections.Counter(HITZ).items()))
print('ARMLEG_FRAMES', an, len(bad), 'of', len(per), bad[:60])
