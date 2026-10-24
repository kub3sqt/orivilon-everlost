"""Chop (boční kácení sekerou) a Mine (krumpáč do země).

Nástroj se animuje přes CTRL_tool (klíčové pózy). Obě dlaně jsou IK na pevných gripech
na násadě (MCH_grip.R nahoře u hlavy, MCH_grip.L dole u hlavice) -> konzistentní úchop.
Trajektorie hlavy nástroje se generuje z polárních parametrů (skutečné oblouky), klíčuje
se v hustších klíčích v úderu. Pro kontrolu: PROXY_Trunk, PROXY_ChopContact, PROXY_MineTarget.
"""
import sys, math, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import anim_lib as A, tools_lib as T, grip_solver as GS
from anim_lib import Rig, E
from mathutils import Vector, Matrix

AXE_EDGE = Vector((0.133, 0.400, 0.0))   # střed ostří v prostoru nástroje
PICK_TIP = Vector((0.185, 0.465, 0.0))   # úderný hrot krumpáče


def frame_from(O, Y, Xhint):
    Y = Vector(Y).normalized()
    X = Vector(Xhint); X = (X - Y * X.dot(Y)).normalized()
    Z = X.cross(Y)
    M = Matrix((X, Y, Z)).transposed().to_4x4(); M.translation = Vector(O)
    return M


def dir_h(deg, pitch=0.0):
    a, p = math.radians(deg), math.radians(pitch)
    return Vector((math.cos(a) * math.cos(p), math.sin(a) * math.cos(p), math.sin(p)))


# ------------------------------------------------------------------ CHOP
PIV = Vector((-0.05, 0.0, 0.0))


def chop_tool(phi, r, z, delta, pitch, roll=0.0):
    """Polární parametry: phi = azimut gripu kolem svislé osy (PIV), delta = natočení násady
    vůči radiále, pitch = sklon násady, roll = natočení ostří kolem násady."""
    O = PIV + dir_h(phi) * r; O.z = z
    th = phi + delta
    Y = dir_h(th, pitch)
    tang = Vector((-math.sin(math.radians(th)), math.cos(math.radians(th)), 0.0))  # směr CCW (zprava doleva)
    X = (tang - Y * tang.dot(Y)).normalized()
    X = Matrix.Rotation(math.radians(roll), 3, Y) @ X
    return frame_from(O, Y, X)


def axe_frame(O, theta, pitch=0.0, roll=0.0):
    """Rám sekery: O = grip pravé ruky, theta = azimut násady (0=+X, 270=vpřed), ostří ve směru
    švihu zprava doleva (tečna CCW)."""
    Y = dir_h(theta, pitch)
    th = math.radians(theta)
    tang = Vector((-math.sin(th), math.cos(th), 0.0))
    X = (tang - Y * tang.dot(Y)).normalized()
    X = Matrix.Rotation(math.radians(roll), 3, Y) @ X
    return frame_from(O, Y, X)


def chop_contact(theta=238.0, pitch=-2.0, roll=0.0):
    M = axe_frame(Vector(), theta, pitch, roll)
    O = T.CHOP_CONTACT - M.to_3x3() @ AXE_EDGE
    return O, theta, pitch, roll


def polar_lerp(Oa, Ob, t, piv=PIV):
    """Interpolace polohy po oblouku kolem svislé osy přes piv (skutečný oblouk švihu)."""
    ra, rb = (Oa - piv).xy, (Ob - piv).xy
    aa, ab = math.atan2(ra.y, ra.x), math.atan2(rb.y, rb.x)
    while ab - aa > math.pi: ab -= 2 * math.pi
    while ab - aa < -math.pi: ab += 2 * math.pi
    # švih je CCW (zprava přes předek doleva): vynuť kladný směr
    if ab < aa: ab += 2 * math.pi
    a = aa + (ab - aa) * t; r = ra.length + (rb.length - ra.length) * t
    return Vector((piv.x + r * math.cos(a), piv.y + r * math.sin(a), Oa.z + (Ob.z - Oa.z) * t))


def chop_keys():
    Oc, thc, pc, rc = chop_contact()
    Ow, thw, pw, rw = Vector((-0.28, -0.09, 0.98)), 118.0, 14.0, -6.0     # nápřah: hlava sekery za pravým ramenem
    K = {}
    K[0] = ((Vector((-0.09, -0.42, 0.74)), 250.0, 48.0, 0.0), 'ready')
    K[5] = ((Vector((-0.27, -0.36, 0.82)), 200.0, 36.0, -4.0), 'wind1')
    K[10] = ((Vector((-0.31, -0.24, 0.94)), 140.0, 20.0, -6.0), 'wind2')
    K[14] = ((Ow, thw, pw, rw), 'windup')
    K[16] = ((Ow + Vector((0.01, -0.01, 0.004)), thw - 3, pw, rw), 'antic')
    for f, t, nm in ((18, 0.22, 'strike1'), (19, 0.52, 'strike2'), (20, 0.80, 'strike3')):
        th = thw + ((thc + 360 if thc < thw else thc) - thw) * t
        K[f] = ((polar_lerp(Ow, Oc, t), th % 360, pw + (pc - pw) * t, rw + (rc - rw) * t), nm)
    K[21] = ((Oc, thc, pc, rc), 'contact')
    Mc = axe_frame(Oc, thc, pc, rc); Xc = Mc.col[0].xyz
    K[23] = ((Oc + Xc * 0.004, thc, pc, rc), 'settle')
    K[26] = ((Oc - Xc * 0.06 + Vector((0, 0.03, 0.01)), thc - 8, pc + 4, rc), 'pull')
    K[30] = ((Vector((-0.05, -0.44, 0.74)), 245.0, 25.0, 0.0), 'recover')
    K[35] = ((Vector((-0.08, -0.42, 0.74)), 250.0, 45.0, 0.0), 'return')
    K[40] = K[0]
    return K


CHOP_BODY = {
    #        hips_loc               hips_rot      spine        chest        neck        head
    'ready':   ((0.000, 0.000, -0.045), (6, 0, -2), (4, 0, -2), (3, 0, -2), (-4, 0, 3), (2, 0, 2)),
    'wind1':   ((-0.012, 0.010, -0.044), (5, -1, -7), (3, 0, -8), (2, 0, -10), (-4, 0, 8), (1, 0, 7)),
    'wind2':   ((-0.025, 0.020, -0.040), (3, -2, -13), (1, -1, -15), (-1, -2, -21), (-3, 0, 15), (0, 0, 13)),
    'windup':  ((-0.030, 0.024, -0.038), (2, -2, -15), (0, -2, -16), (-2, -3, -24), (-3, 0, 18), (0, 0, 16)),
    'antic':   ((-0.032, 0.026, -0.040), (2, -2, -16), (0, -2, -17), (-3, -3, -25), (-3, 0, 19), (0, 0, 17)),
    'strike1': ((-0.016, 0.012, -0.050), (4, -1, -6), (2, -1, -14), (0, -2, -21), (-3, 0, 15), (1, 0, 13)),
    'strike2': ((0.002, 0.000, -0.058), (6, 0, 1), (4, 0, -8), (2, -1, -14), (-3, 0, 9), (2, 0, 8)),
    'strike3': ((0.014, -0.010, -0.064), (8, 1, 5), (6, 0, -3), (4, 0, -7), (-3, 0, 4), (3, 0, 3)),
    'contact': ((0.020, -0.016, -0.068), (9, 1, 6), (7, 0, -1), (6, 0, -3), (-3, 0, 1), (4, 0, 1)),
    'settle':  ((0.022, -0.018, -0.070), (10, 1, 6), (8, 0, -1), (7, 0, -3), (-3, 0, 1), (4, 0, 1)),
    'pull':    ((0.016, -0.010, -0.062), (8, 1, 4), (6, 0, -2), (5, 0, -3), (-3, 0, 2), (3, 0, 2)),
    'recover': ((0.006, 0.000, -0.050), (7, 0, 0), (5, 0, -2), (4, 0, -2), (-4, 0, 3), (2, 0, 2)),
    'return':  ((0.002, 0.000, -0.046), (6, 0, -2), (4, 0, -2), (3, 0, -2), (-4, 0, 3), (2, 0, 2)),
}

CHOP_FEET = {'.L': ((0.07, -0.10, 0.0), -14), '.R': ((-0.06, 0.09, 0.0), -24)}


def yaw_sum(b):
    return b[1][2] + b[2][2] + b[3][2]


def build_chop(rig):
    ob = rig.ob
    prev = None; first = {}
    solver = A.Solver(ob, bpy.data.objects['Tool_Axe'])
    act = A.new_action(ob, 'Chop')
    K = chop_keys()
    for f in sorted(K):
        params, bname = K[f]
        b = CHOP_BODY[bname]
        Mt = axe_frame(*params)
        rig.reset()
        rig.ik_arms(1.0)
        rig.hips(b[0], b[1])
        for k, v in zip(('spine', 'chest', 'neck', 'head'), b[2:]):
            rig.fk(k, v)
        for s, (loc, yaw) in CHOP_FEET.items():
            rig.foot(s, loc=loc, yaw=yaw)
        # kolena: drž póly ve směru chodidel
        if bname not in ('contact', 'settle'):
            rig.tool(Mt); A.solve_reach(rig, Mt)
        else:
            rig.tool(Mt); A.solve_reach(rig, Mt)
        opt = chop_opt(bname)
        prev = grip_and_key(rig, Mt, f, prev, first, opt)
    solver.remove()
    refine_grips(rig, 0, 40, set(K))
    A.finalize(act, 0, 40, cyclic=True)
    act['contact_frame'] = 21
    act['tool'] = 'Axe'
    return act


# ------------------------------------------------------------------ úchop (v002)
FING, THUMB = 82, 42


def grip_and_key(rig, Mt, f, prev, first, opt=None):
    """Sevřená pěst, natočení obou rukou kolem násady + póly loktů, dosah, klíč."""
    rig.grip('.L', FING, THUMB); rig.grip('.R', FING, THUMB)
    if opt is not None and not (f == 40 and first):
        pivot, mr, mt = opt
        rig.tool(Mt); A.solve_reach(rig, Mt)
        Mt = GS.optimize_tool(rig, Mt, pivot(Mt), mr, 6.0 if mr <= 12 else 9.0, mt, verbose=True)
        rig.tool(Mt); A.solve_reach(rig, Mt)
    if f == 40 and first:  # smyčka: poslední snímek = první
        for n, (l, q) in first.items():
            rig.P[n].location = l; rig.P[n].rotation_quaternion = q
    else:
        prev = GS.solve(rig, Mt, prev)
        A.solve_reach(rig, Mt)
        prev = GS.solve(rig, Mt, prev, verbose=True)
        if f == 0:
            for n in ('MCH_grip.L', 'MCH_grip.R', 'CTRL_elbow.L', 'CTRL_elbow.R', 'hips', 'CTRL_tool'):
                first[n] = (rig.P[n].location.copy(), rig.P[n].rotation_quaternion.copy())
    rig.key(f)
    return prev


def chop_opt(bname):
    if bname in ('contact', 'settle', 'strike3'):
        return (lambda M: M @ AXE_EDGE, 24.0, 0.0)
    if bname in ('windup', 'antic', 'wind2', 'strike1', 'strike2', 'strike3'):
        return (lambda M: M.translation.copy(), 12.0, 0.0)
    return (lambda M: M.translation.copy(), 18.0, 0.03)


def mine_opt(bname):
    if bname in ('impact', 'settle', 'strike3'):
        return (lambda M: M @ PICK_TIP, 27.0, 0.0)
    if bname in ('lever', 'ready', 'recover'):
        return (lambda M: M @ PICK_TIP, 27.0, 0.04)
    if bname == 'lift_b':
        return (lambda M: M.translation.copy(), 24.0, 0.0)
    if bname in ('lift', 'strike2'):
        return (lambda M: M.translation.copy(), 10.0, 0.0)
    return None  # nápřah nad ramenem: dráha hlavy krumpáče mimo klobouk zůstává přesně podle klíčů


def refine_grips(rig, f0, f1, keyframes):
    """Breakdowny po snímcích: v interpolovaných mezisnímcích znovu vyřeš natočení pěstí a lokty,
    aby obě ruce držely násadu po celý cyklus (nejen v klíčových pózách)."""
    sc = bpy.context.scene; ob = rig.ob
    prev = None
    bones = ('MCH_grip.L', 'MCH_grip.R', 'CTRL_elbow.L', 'CTRL_elbow.R')
    for f in range(f0, f1):
        sc.frame_set(f)
        Mt = A.fk_matrix(ob, 'CTRL_tool')
        prev = GS.solve(rig, Mt, prev)
        if f not in keyframes:
            rig.key(f, bones=bones, roll=False, constraints=False)


# ------------------------------------------------------------------ MINE
PIV_M = Vector((-0.17, 0.02, 0.80))


def mine_tool(psi, r, kappa, x, roll=0.0, xtilt=0.0):
    """Svislá rovina úderu (x = boční poloha gripu). psi = úhel gripu kolem PIV_M
    (0 = vpřed, + nahoru), kappa = úhel násady vůči radiále, xtilt = sbíhání roviny k cíli."""
    p = math.radians(psi)
    O = Vector((x, PIV_M.y - math.cos(p) * r, PIV_M.z + math.sin(p) * r))
    a = math.radians(psi + kappa)
    Y = Vector((math.sin(math.radians(xtilt)), -math.cos(a), math.sin(a))).normalized()
    # hrot míří ve směru pohybu dolů (klesající psi): kolmice na Y v rovině úderu
    X = Vector((0.0, -math.sin(a), -math.cos(a)))
    X = Matrix.Rotation(math.radians(roll), 3, Y) @ X
    return frame_from(O, Y, X)


def mine_frame(O, a_deg, xtilt=0.0, roll=0.0):
    """Rám krumpáče: O = grip pravé ruky, a_deg = sklon násady v sagitální rovině
    (0 = vodorovně vpřed, + nahoru, 90 = svisle, >90 = dozadu), xtilt = vyklonění násady do strany
    (záporné = ven doprava, mimo klobouk). Hrot míří ve směru švihu dolů."""
    a = math.radians(a_deg)
    Y = Vector((math.sin(math.radians(xtilt)), -math.cos(a), math.sin(a))).normalized()
    X = Vector((0.0, -math.sin(a), -math.cos(a)))
    X = Matrix.Rotation(math.radians(roll), 3, Y) @ X
    return frame_from(O, Y, X)


def mine_contact(a_deg=-24.0, xtilt=0.0, roll=0.0):
    M = mine_frame(Vector(), a_deg, xtilt, roll)
    O = T.MINE_TARGET - M.to_3x3() @ PICK_TIP
    return O, a_deg, xtilt, roll


def mine_keys():
    Oi, ai, xi, ri = mine_contact()
    V_ = Vector
    K = {}
    K[0] = ((Oi + V_((0.0, -0.025, 0.035)), ai - 2, 0, 0), 'ready')
    K[3] = ((V_((-0.17, -0.40, 0.62)), 0, -12, 0), 'lift_b')
    K[6] = ((V_((-0.24, -0.29, 0.86)), 45, -26, 0), 'lift')
    K[12] = ((V_((-0.33, -0.12, 1.00)), 115, -34, 0), 'wind2')
    K[15] = ((V_((-0.33, -0.06, 1.01)), 145, -35, 0), 'windup')
    K[17] = ((V_((-0.34, -0.05, 1.02)), 149, -36, 0), 'antic')
    K[19] = ((V_((-0.35, -0.21, 0.99)), 100, -41, 0), 'strike1')
    K[20] = ((V_((-0.27, -0.37, 0.86)), 40, -22, 0), 'strike2')
    K[21] = ((Oi.lerp(V_((-0.14, -0.40, 0.66)), 0.5), -12, -5, 0), 'strike3')
    K[22] = ((Oi, ai, xi, ri), 'impact')
    K[24] = ((Oi + V_((0, 0.004, -0.003)), ai, xi, ri), 'settle')
    K[28] = ((Oi + V_((0, -0.02, 0.02)), ai, xi, ri), 'lever')
    K[33] = ((Oi + V_((0.0, -0.025, 0.04)), ai - 2, 0, 0), 'recover')
    K[40] = K[0]
    return K


MINE_BODY = {
    'ready':   ((0.000, 0.045, -0.070), (30, 0, 2), (21, 0, 1), (14, 0, 0), (-16, 0, 0), (-11, 0, 0)),
    'lift_b':  ((-0.006, 0.030, -0.075), (15, -1, 0), (10, 0, -2), (6, -1, -3), (-9, 0, 2), (-6, 0, 2)),
    'lift':    ((-0.010, 0.030, -0.030), (4, -1, -3), (1, 0, -4), (-2, -1, -5), (-4, 0, 4), (-3, 0, 3)),
    'wind2':   ((-0.016, 0.040, -0.010), (-3, -2, -6), (-5, -1, -6), (-8, -2, -7), (2, 0, 6), (2, 0, 5)),
    'windup':  ((-0.018, 0.045, -0.006), (-5, -2, -7), (-7, -1, -7), (-10, -2, -8), (4, 0, 7), (3, 0, 6)),
    'antic':   ((-0.018, 0.046, -0.008), (-5, -2, -7), (-8, -1, -7), (-11, -2, -8), (4, 0, 7), (3, 0, 6)),
    'strike1': ((-0.012, 0.035, -0.040), (6, -1, -4), (4, -1, -4), (2, -1, -4), (-2, 0, 4), (0, 0, 3)),
    'strike2': ((-0.006, 0.030, -0.080), (16, 0, -1), (12, 0, -1), (9, 0, -1), (-8, 0, 1), (-5, 0, 1)),
    'strike3': ((-0.002, 0.040, -0.068), (27, 0, 1), (19, 0, 1), (13, 0, 0), (-15, 0, 0), (-10, 0, 0)),
    'impact':  ((0.000, 0.048, -0.075), (31, 0, 2), (22, 0, 1), (15, 0, 0), (-17, 0, 0), (-12, 0, 0)),
    'settle':  ((0.000, 0.049, -0.078), (32, 0, 2), (22, 0, 1), (15, 0, 0), (-17, 0, 0), (-12, 0, 0)),
    'lever':   ((0.000, 0.047, -0.073), (31, 0, 2), (21, 0, 1), (14, 0, 0), (-16, 0, 0), (-11, 0, 0)),
    'recover': ((0.000, 0.045, -0.070), (30, 0, 2), (21, 0, 1), (14, 0, 0), (-16, 0, 0), (-11, 0, 0)),
}
MINE_FEET = {'.L': ((0.11, -0.08, 0.0), -10), '.R': ((-0.10, 0.07, 0.0), -18)}


def build_mine(rig):
    ob = rig.ob
    prev = None; first = {}
    solver = A.Solver(ob, bpy.data.objects['Tool_Pickaxe'])
    act = A.new_action(ob, 'Mine')
    K = mine_keys()
    for f in sorted(K):
        params, bname = K[f]
        b = MINE_BODY[bname]
        rig.reset(); rig.ik_arms(1.0)
        rig.hips(b[0], b[1])
        for k, v in zip(('spine', 'chest', 'neck', 'head'), b[2:]):
            rig.fk(k, v)
        for s, (loc, yaw) in MINE_FEET.items():
            rig.foot(s, loc=loc, yaw=yaw)
        Mt = mine_frame(*params)
        rig.tool(Mt); A.solve_reach(rig, Mt)
        opt = mine_opt(bname)
        prev = grip_and_key(rig, Mt, f, prev, first, opt)
    solver.remove()
    refine_grips(rig, 0, 40, set(K))
    A.finalize(act, 0, 40, cyclic=True)
    act['contact_frame'] = 22
    act['tool'] = 'Pickaxe'
    return act


if __name__ == '__main__':
    src, dst = sys.argv[-2], sys.argv[-1]
    bpy.ops.wm.open_mainfile(filepath=src)
    ob = bpy.data.objects['Traveler_Rig']
    rig = Rig(ob)
    build_chop(rig)
    build_mine(rig)
    bpy.ops.wm.save_as_mainfile(filepath=dst)
