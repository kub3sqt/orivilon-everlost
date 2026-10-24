"""Swim – prsa (breaststroke), in-place, tělo vodorovně čelem dolů, hlava nad hladinou.
Styl zvolen kvůli velké hlavě a budoucímu klobouku: paže pracují pod bradou a do stran,
nikdy přes hlavu (kraul by hlavou procházel). Záběr: glide -> outsweep -> insweep (nádech)
-> recovery vpřed; nohy: přitažení -> žabí kop -> splývání.
Hladina vody (doporučení pro Unity): z ≈ 0.80 m nad rootem (ramena těsně pod hladinou).
"""
import sys, math, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import anim_lib as A, pose_lib as PL
from anim_lib import Rig
from mathutils import Vector, Matrix, Quaternion
import body_sdf as S

T = 36
BODY_PITCH = 78.0


def arm_aim(rig, side, upper_dir, fore_dir, wrist=(0, 0, 0), fingers=5, thumb=0):
    """Paže z cílových směrů (v rest rámci hrudníku): nadloktí -> upper_dir, předloktí -> fore_dir."""
    sgn = 1.0 if side == '.L' else -1.0
    u = Vector((upper_dir[0] * sgn, upper_dir[1], upper_dir[2])).normalized()
    f = Vector((fore_dir[0] * sgn, fore_dir[1], fore_dir[2])).normalized()
    pb = rig.P['upper_arm' + side]
    d0 = (pb.bone.tail_local - pb.bone.head_local).normalized()
    W = d0.rotation_difference(u).to_matrix()
    z_now = W @ pb.bone.matrix_local.to_3x3().col[2]
    b = f - u * f.dot(u)
    tw = 0.0
    if b.length > 1e-4:
        b.normalize()
        z_now = (z_now - u * z_now.dot(u)).normalized()
        tw = math.atan2(z_now.cross(b).dot(u), z_now.dot(b))
    W = Quaternion(u, tw).to_matrix() @ W
    PL.rot_world(pb, W)
    PL.local_x(rig.P['forearm' + side], math.degrees(u.angle(f)))
    PL.rot_world(rig.P['hand' + side], PL.eul(wrist[0], wrist[1] * sgn, wrist[2] * sgn))
    rig.grip(side, fingers, thumb)


def body_matrix(loc, pitch):
    h = rig_ob.data.bones['hips'].head_local
    return Matrix.Translation(Vector(loc)) @ Matrix.Translation(h) @ Matrix.Rotation(math.radians(pitch), 4, 'X') @ Matrix.Translation(-h)


def leg(rig, side, B, ankle_off, foot_dir, foot_up, pole_dir):
    """ankle_off: kotník relativně ke kyčelnímu kloubu (rest rámec těla); foot_dir/up v rest rámci."""
    sgn = 1.0 if side == '.L' else -1.0
    m = lambda v: Vector((v[0] * sgn, v[1], v[2]))
    hip = Vector((S.P['hip'][0] * sgn, S.P['hip'][1], S.P['hip'][2]))
    ank = hip + m(ankle_off)
    L = 0.488
    if (ank - hip).length > L:
        ank = hip + (ank - hip).normalized() * L
    y = m(foot_dir).normalized(); z = m(foot_up); z = (z - y * z.dot(y)).normalized(); x = y.cross(z)
    F = Matrix((x, y, z)).transposed().to_4x4(); F.translation = ank
    Fw = B @ F
    rig.foot_world(side, Fw)
    # pól kolena: posuň CTRL_knee na požadované místo (výpočet přes nově nastavenou matici rodiče)
    knee_mid = (hip + ank) * 0.5
    P_w = B @ (knee_mid + m(pole_dir).normalized() * 0.5)
    pk = rig.P['CTRL_knee' + side]
    parentM = rig.P['CTRL_foot' + side].matrix if False else None
    Cfoot = rig.ob.data.bones['CTRL_foot' + side].matrix_local
    C0 = rig.ob.data.bones['CTRL_foot' + side]
    Mc = Fw @ (Cfoot.inverted() @ rig.ob.data.bones['foot' + side].matrix_local).inverted()
    rest_rel = Cfoot.inverted() @ pk.bone.matrix_local
    desired = (Mc @ rest_rel).copy(); desired.translation = P_w
    basis = (Mc @ rest_rel).inverted() @ desired
    pk.location = basis.translation; pk.rotation_quaternion = (1, 0, 0, 0)



# ---------------------------------------------------------------- v003: nohy z anatomických úhlů
# Ve v002 se nohy klíčovaly jen v 8 pózách přes cílový kotník + pól (IK). Mezi klíči se kotník
# i pól interpolovaly lineárně ve světě, takže koleno v přechodu f11->f15 uhýbalo (rovina kolena
# se stáčela až o 17° za půl snímku), kotník se z natažené špičky (plantární flexe 55°) za dva
# snímky přehnul do dorzální flexe 58° a v kopu f23->f25 se noha propnula do 0° (IK „cvak“).
# Nově: každý snímek z plynule (Catmull-Rom, cyklicky) interpolovaných úhlů kyčle/kolena/kotníku
# spočítáme dopřednou kinematikou polohu kotníku, orientaci chodidla a směr čéšky, a teprve
# tím nastavíme IK ovladače. Koleno tak jde po hladké dráze, pól je vždy přesně před čéškou
# a úhly zůstávají v anatomickém rozsahu (koleno 10–80°, kotník −12..+42° vůči rest).
#            flex  abd  inrot knee plantar   (stupně; flex = kyčel vpřed, abd = do strany,
#                                             inrot = vnitřní rotace stehna -> chodidla ven za koleny)
LEGA = {0: (6, 4, 0, 12, 40), 6: (6, 4, 0, 12, 40), 11: (14, 8, 6, 30, 30), 15: (36, 18, 20, 72, 0),
        19: (42, 22, 24, 78, -8), 23: (26, 28, 22, 42, -12), 27: (10, 10, 6, 16, 32), 31: (6, 4, 0, 12, 40)}
LEGA[T] = LEGA[0]


def _catmull(keys, f, period):
    ks = sorted(k for k in keys if k < period)
    n = len(ks)
    f = f % period
    i = max(j for j in range(n) if ks[j] <= f)
    k0, k1 = ks[i], ks[(i + 1) % n]
    span = (k1 - k0) % period or period
    t = ((f - k0) % period) / span
    km, k2 = ks[(i - 1) % n], ks[(i + 2) % n]
    # nestejnoměrné rozestupy: tečny škálované délkou úseků (Kochanek-Bartels bez napětí)
    pm, p0, p1, p2 = (keys[km], keys[k0], keys[k1], keys[k2])
    d0 = (k0 - km) % period or period; d2 = (k2 - k1) % period or period
    out = []
    for c in range(len(p0)):
        m0 = (p1[c] - pm[c]) / (span + d0) * span
        m1 = (p2[c] - p0[c]) / (span + d2) * span
        t2, t3 = t * t, t * t * t
        out.append((2 * t3 - 3 * t2 + 1) * p0[c] + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1[c] + (t3 - t2) * m1)
    return out


def leg_fk(rig, side, B, flex, abd, inrot, knee, plantar):
    sgn = 1.0 if side == '.L' else -1.0
    b = rig.ob.data.bones
    th, sh, ft = b['thigh' + side], b['shin' + side], b['foot' + side]
    H = th.head_local
    R = lambda ax, d: Matrix.Rotation(math.radians(d), 3, ax)
    # vnitřní rotace kolem osy stehna (čéška k ose těla), flexe vpřed (-Y), abdukce do strany
    t_ax = (th.tail_local - th.head_local).normalized()
    Wt = R('Y', -abd * sgn) @ R('X', -flex) @ Matrix.Rotation(math.radians(-inrot * sgn), 3, t_ax)
    K = H + Wt @ (th.tail_local - th.head_local)
    kx = (Wt @ Vector((1, 0, 0))).normalized()
    Ws = Matrix.Rotation(math.radians(knee - 7.0), 3, kx) @ Wt   # rest koleno je už ~7° ohnuté
    A_ = K + Ws @ (sh.tail_local - sh.head_local)
    Wf = Matrix.Rotation(math.radians(plantar), 3, (Ws @ Vector((1, 0, 0))).normalized()) @ Ws
    F = (Wf @ ft.matrix_local.to_3x3()).to_4x4(); F.translation = A_
    Fw = B @ F
    rig.foot_world(side, Fw)
    cap = (Wt @ Vector((0, -1, 0))).normalized()
    P_w = B @ (K + cap * 0.45)
    pk = rig.P['CTRL_knee' + side]
    Cfoot = rig.ob.data.bones['CTRL_foot' + side].matrix_local
    Mc = Fw @ (Cfoot.inverted() @ ft.matrix_local).inverted()
    rest_rel = Cfoot.inverted() @ pk.bone.matrix_local
    desired = (Mc @ rest_rel).copy(); desired.translation = P_w
    basis = (Mc @ rest_rel).inverted() @ desired
    pk.location = basis.translation; pk.rotation_quaternion = (1, 0, 0, 0)


# klíčové pózy: (frame, body_pitch_add, chest/neck/head, arms (upper, fore, wrist), legs (ankle, foot_dir, foot_up, pole))
# v004: paže ve splývání o ~15° níž (flexe ramene ~150° místo ~166°) – při téměř vzpažení se látka
# tuniky v ohbí ramene překládala rubem ven; silueta prsou zůstává (paže pod bradou, vpřed)
EXT = ((0.16, -0.45, 0.88), (0.12, -0.42, 0.90))  # paže natažené vpřed pod bradou
POINT = ((0.0, 0.10, -1.0), (0.0, -1.0, 0.0))     # špička natažená
LEGS_GLIDE = ((0.025, 0.04, -0.485), POINT[0], POINT[1], (0.4, -0.9, 0.0))
LEGS_PREP = ((0.10, 0.10, -0.47), POINT[0], POINT[1], (0.5, -0.8, 0.0))
LEGS_RECOV = ((0.20, 0.26, -0.28), (0.75, -0.25, 0.35), (0.0, -0.2, 1.0), (0.6, -0.8, 0.0))
LEGS_KICK = ((0.26, 0.08, -0.40), (0.70, -0.10, -0.55), (0.0, -0.6, 0.6), (0.6, -0.8, 0.0))
LEGS_CLOSE = ((0.07, 0.05, -0.48), (0.25, 0.05, -1.0), (0.0, -1.0, 0.0), (0.5, -0.85, 0.0))

KEYS = {
    0: dict(pitch=0, chest=(0, 0, 0), neck=(-30, 0, 0), head=(-26, 0, 0), hz=0.000,
            arm=(EXT[0], EXT[1], (0, 0, 0)), legs=LEGS_GLIDE),
    6: dict(pitch=0, chest=(-2, 0, 0), neck=(-31, 0, 0), head=(-27, 0, 0), hz=0.000,
            arm=((0.55, -0.20, 0.80), (0.60, -0.25, 0.76), (0, 0, 0)), legs=LEGS_GLIDE),
    11: dict(pitch=-4, chest=(-6, 0, 0), neck=(-34, 0, 0), head=(-28, 0, 0), hz=0.012,
             arm=((0.80, -0.35, 0.48), (0.20, -0.92, 0.32), (10, 0, 0)), legs=LEGS_PREP),
    15: dict(pitch=-7, chest=(-9, 0, 0), neck=(-36, 0, 0), head=(-28, 0, 0), hz=0.022,
             arm=((0.62, -0.62, 0.30), (-0.25, -0.70, 0.62), (0, 0, 0)), legs=LEGS_RECOV),
    19: dict(pitch=-5, chest=(-7, 0, 0), neck=(-34, 0, 0), head=(-28, 0, 0), hz=0.015,
             arm=((0.30, -0.60, 0.65), (0.0, -0.42, 0.90), (0, 0, 0)), legs=LEGS_RECOV),
    23: dict(pitch=-1, chest=(-2, 0, 0), neck=(-31, 0, 0), head=(-27, 0, 0), hz=0.004,
             arm=((0.17, -0.47, 0.87), (0.12, -0.44, 0.89), (0, 0, 0)), legs=LEGS_KICK),
    27: dict(pitch=1, chest=(1, 0, 0), neck=(-30, 0, 0), head=(-26, 0, 0), hz=-0.006,
             arm=(EXT[0], EXT[1], (0, 0, 0)), legs=LEGS_CLOSE),
    31: dict(pitch=1, chest=(1, 0, 0), neck=(-30, 0, 0), head=(-26, 0, 0), hz=-0.004,
             arm=(EXT[0], EXT[1], (0, 0, 0)), legs=LEGS_GLIDE),
}
KEYS[T] = KEYS[0]


def build(rig):
    global rig_ob
    rig_ob = rig.ob
    act = A.new_action(rig.ob, 'Swim')
    for f in sorted(KEYS):
        k = KEYS[f]
        rig.reset()
        pitch = BODY_PITCH + k['pitch']
        loc = (0.0, 0.0, k['hz'])
        rig.hips(loc, (pitch, 0, 0))
        rig.fk('spine', (k['chest'][0] * 0.5, 0, 0))
        rig.fk('chest', k['chest'])
        rig.fk('neck', k['neck']); rig.fk('head', k['head'])
        for s in ('.L', '.R'):
            up, fo, wr = k['arm']
            arm_aim(rig, s, up, fo, wr)
        B = body_matrix(loc, pitch)
        for s in ('.L', '.R'):
            leg_fk(rig, s, B, *LEGA[f])
        rig.key(f)
    A.finalize(act, 0, T, cyclic=True)
    # v003: nohy klíčované v každém snímku z interpolovaných anatomických úhlů (viz leg_fk)
    sc = bpy.context.scene
    hb = rig.ob.data.bones['hips']
    LB = ['CTRL_foot.L', 'CTRL_foot.R', 'CTRL_knee.L', 'CTRL_knee.R']
    for f in range(0, T):
        sc.frame_set(f)
        B = rig.P['hips'].matrix @ hb.matrix_local.inverted()
        ang = _catmull(LEGA, f, T)
        for s in ('.L', '.R'):
            leg_fk(rig, s, B, *ang)
        rig.key(f, bones=LB, roll=False, constraints=False)
    for s in ('.L', '.R'):
        pass
    A.finalize(act, 0, T, cyclic=True)
    # poslední snímek smyčky = první
    sc.frame_set(0)
    act['in_place'] = True
    act['forward_axis_blender'] = '-Y'
    act['speed_mps'] = 0.0
    act['recommended_swim_speed_mps'] = 0.75
    act['water_level_z'] = 0.80
    return act


if __name__ == '__main__':
    src, dst = sys.argv[-2], sys.argv[-1]
    bpy.ops.wm.open_mainfile(filepath=src)
    rig = Rig(bpy.data.objects['Traveler_Rig'])
    build(rig)
    bpy.ops.wm.save_as_mainfile(filepath=dst)
