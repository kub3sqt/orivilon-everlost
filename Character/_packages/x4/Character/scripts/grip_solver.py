"""v002 – řešič úchopu obouručního nástroje.

Problém v001: dlaně měly vůči násadě pevný úhel, takže IK muselo zápěstí „zlomit“ a předloktí
se kroutilo. Ruka ale může kolem násady volně rotovat (jako skutečná pěst na topůrku).

Pro každou ruku a každý řešený snímek hledáme:
  * theta – natočení pěsti kolem osy násady (v místě úchopu),
  * swivel lokte – analyticky tak, aby předloktí mířilo co nejvíc ve směru dlaně (rovné zápěstí),
a minimalizujeme: ohnutí zápěstí + přetočení předloktí mimo anatomický rozsah + loket v trupu
+ nedosah + skok proti předchozímu snímku. Výsledek se zapíše do:
  * MCH_grip.L/R (rotace kolem násady – kosti jsou děti CTRL_tool),
  * CTRL_elbow.L/R (pól IK, aby loket skončil přesně v řešené poloze).
"""
import math
from mathutils import Vector, Matrix, Quaternion
import anim_lib as A
import body_sdf as S

GRIP_Y = {'.R': 0.0}


def _setup(ob):
    import rig_lib as R
    GRIP_Y['.L'] = R.TOOL_GRIP_L_Y


def grip_world(ob, Mt, side, theta):
    """Světová matice kosti ruky při natočení theta (rad) kolem osy násady."""
    b = ob.data.bones
    Ct = b['CTRL_tool'].matrix_local
    G0 = Ct.inverted() @ b['MCH_grip' + side].matrix_local
    p = Vector((0, GRIP_Y[side], 0))
    Rt = Matrix.Translation(p) @ Matrix.Rotation(theta, 4, 'Y') @ Matrix.Translation(-p)
    return Mt @ Rt @ G0, Rt, G0


def set_grip_basis(rig, side, Rt, G0):
    # basis = G0^-1 Rt G0  (rotace kolem násady vyjádřená v prostoru kosti gripu)
    B = G0.inverted() @ Rt @ G0
    pb = rig.P['MCH_grip' + side]
    loc, rot, _ = B.decompose()
    pb.location = loc; pb.rotation_quaternion = rot


def _signed(a, b, axis):
    return math.atan2(a.cross(b).dot(axis), a.dot(b))


def forearm_hand_twist(ob, side, S_, E, W, Hr):
    u = (E - S_); fa = (W - E)
    if u.length < 1e-6 or fa.length < 1e-6:
        return None
    u.normalize(); fa.normalize()
    Xf = u.cross(fa)
    if Xf.length < 1e-4:
        return None
    Xf.normalize(); Zf = Xf.cross(fa)
    F = Matrix((Xf, fa, Zf)).transposed()
    b = ob.data.bones
    F0 = b['forearm' + side].matrix_local.to_3x3(); H0 = b['hand' + side].matrix_local.to_3x3()
    R0 = F0.inverted() @ H0
    D = (F.inverted() @ Hr) @ R0.inverted()
    q = D.to_quaternion()
    tw = math.degrees(2 * math.atan2(q.y, q.w))
    return (tw + 180) % 360 - 180


def _rest_twist(ob, side):
    b = ob.data.bones
    F = b['forearm' + side].matrix_local.to_3x3(); H = b['hand' + side].matrix_local.to_3x3()
    ax = F.col[1].normalized()
    xf = (F.col[0] - ax * F.col[0].dot(ax)).normalized()
    xh = (H.col[0] - ax * H.col[0].dot(ax)).normalized()
    return _signed(xf, xh, ax)


TORSO = [('chest', S.P['chest_c'], S.P['chest_r']), ('spine', S.P['belly_c'], S.P['belly_r']),
         ('hips', S.P['pelvis_c'], S.P['pelvis_r']), ('head', S.P['head_c'], S.P['head_r'])]


def _inside_torso(ob, cache, pts, rad):
    worst = 0.0
    for bn, c, r in TORSO:
        M = A.fk_matrix(ob, bn, cache) @ ob.data.bones[bn].matrix_local.inverted()
        Mi = M.inverted()
        for p in pts:
            q = Mi @ p
            k = Vector(((q.x - c[0]) / r[0], (q.y - c[1]) / r[1], (q.z - c[2]) / r[2])).length
            d = (k - 1.0) * min(r)
            worst = max(worst, rad - d)
    return worst


def solve_side(rig, Mt, side, prev=None, step_deg=8, verbose=False, apply=True, sw_step=10):
    """prev = (theta, swivel) z předchozího snímku (spojitost)."""
    ob = rig.ob
    cache = {}
    S_ = A.fk_matrix(ob, 'upper_arm' + side, cache).translation
    Lu = ob.data.bones['upper_arm' + side].length
    Lf = ob.data.bones['forearm' + side].length
    best = None
    th_c = list(range(-180, 180, step_deg)); sw_c = list(range(-100, 101, sw_step))
    if prev is not None:
        pt, ps = math.degrees(prev[0]), math.degrees(prev[1])
        th_c = sorted(set(th_c + [int(round(pt)) + k for k in range(-10, 11, 2)]))
        sw_c = sorted(set(sw_c + [int(round(ps)) + k for k in range(-10, 11, 2)]))
    for deg in th_c:
        th = math.radians(deg)
        Hw, Rt, G0 = grip_world(ob, Mt, side, th)
        W = Hw.translation; Hr = Hw.to_3x3()
        f = Hr.col[1].normalized()
        d_vec = W - S_; d = d_vec.length
        base = 0.0
        if d > (Lu + Lf) * 0.995:
            base += 4000 * (d - (Lu + Lf) * 0.995)
            d_eff = (Lu + Lf) * 0.995
        else:
            d_eff = d
        if d < 0.14:
            base += 50
        dirv = d_vec.normalized()
        a = (Lu * Lu - Lf * Lf + d_eff * d_eff) / (2 * d_eff)
        h = math.sqrt(max(Lu * Lu - a * a, 0.0))
        fp = f - dirv * f.dot(dirv)
        if fp.length < 1e-4:
            continue
        n0 = (-fp).normalized()
        for sw in sw_c:
            n = Quaternion(dirv, math.radians(sw)) @ n0
            E = S_ + dirv * a + n * h
            fa = (W - E).normalized()
            bend = math.degrees(fa.angle(f))
            tw = forearm_hand_twist(ob, side, S_, E, W, Hr)
            if tw is None:
                continue
            cost = base
            cost += 0.3 * bend + 2.5 * max(0.0, bend - 28)          # zápěstí: do ~28° v pohodě
            cost += 0.1 * abs(tw) + 2.0 * max(0.0, abs(tw) - 60)    # pronace/supinace do ~60°
            # v004: tvrdší strop – ohyb zápěstí nad ~40° dělal rýhu na hřbetu ruky, twist nad 95° škrtil zápěstí
            cost += 8.0 * max(0.0, bend - 40) + 4.0 * max(0.0, abs(tw) - 95)
            if E.z > S_.z + 0.02:                                   # loket ne nad rameno
                cost += 150 * (E.z - S_.z)
            pen = _inside_torso(ob, cache, [E, E.lerp(W, 0.5)], 0.05)
            cost += 3000 * max(pen, 0.0)
            if prev is not None:
                cost += 0.3 * abs((deg - math.degrees(prev[0]) + 180) % 360 - 180)
                cost += 0.3 * abs(sw - math.degrees(prev[1]))
            if best is None or cost < best[0]:
                best = (cost, th, math.radians(sw), E, n, Rt, G0, bend, tw)
    cost, th, sw, E, n, Rt, G0, bend, tw = best
    if not apply:
        return (th, sw), bend, cost
    set_grip_basis(rig, side, Rt, G0)
    rig.elbow_pole(side, E + n * 0.35)
    if verbose:
        print(f'   grip{side} theta={math.degrees(th):.0f} swivel={math.degrees(sw):.0f} bend={bend:.1f} twist={tw:.0f} cost={cost:.1f}')
    return (th, sw), bend, cost


def solve(rig, Mt, prev=None, verbose=False):
    _setup(rig.ob)
    prev = prev or {}
    out = {}
    for s in ('.R', '.L'):
        out[s] = solve_side(rig, Mt, s, prev.get(s), verbose=verbose)[0]
    return out


def optimize_tool(rig, Mt, pivot, max_rot=18.0, rot_step=9.0, max_tr=0.0, tr_step=0.03, verbose=False):
    """Animátorská korekce pózy nástroje: malé pootočení (a posun) kolem pivotu tak, aby obě ruce
    mohly násadu držet s co nejpřirozenějším zápěstím a předloktím. Vrací upravenou matici."""
    _setup(rig.ob)
    pv = Vector(pivot)
    R3 = Mt.to_3x3()
    angs = [k * rot_step for k in range(-int(max_rot // rot_step), int(max_rot // rot_step) + 1)]
    trs = [0.0] if max_tr <= 0 else [k * tr_step for k in range(-int(max_tr // tr_step), int(max_tr // tr_step) + 1)]
    best = None
    for ax in angs:
        for ay in angs:
            for az in angs:
                Rl = (Matrix.Rotation(math.radians(ax), 3, 'X') @ Matrix.Rotation(math.radians(ay), 3, 'Y') @ Matrix.Rotation(math.radians(az), 3, 'Z'))
                Rw = R3 @ Rl @ R3.inverted()  # rotace v osách nástroje
                for tx in trs:
                    for tz in trs:
                        M = Matrix.Translation(pv) @ Rw.to_4x4() @ Matrix.Translation(-pv) @ Mt
                        M.translation += R3 @ Vector((tx, 0, tz))
                        c = sum(solve_side(rig, M, s, None, step_deg=15, apply=False, sw_step=20)[2] for s in ('.R', '.L'))
                        hp = [M @ Vector((0, y, 0)) for y in (-0.215, -0.10, 0.05)]
                        c += 3000 * max(0.0, _inside_torso(rig.ob, {}, hp, 0.03))
                        c += 0.4 * (abs(ax) + abs(ay) + abs(az)) + 100 * (abs(tx) + abs(tz))
                        if best is None or c < best[0]:
                            best = (c, M, (ax, ay, az, tx, tz))
    if verbose:
        print('   tool opt', [round(x, 3) for x in best[2]], round(best[0], 1))
    return best[1]
