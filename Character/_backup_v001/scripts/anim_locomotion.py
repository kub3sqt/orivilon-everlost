"""Walk + Run (in-place). Postava kráčí ve směru -Y (Blender) = +Z (Unity).

Mechanika chodidel (rozhodující pro směr pohybu):
  * oporová fáze: CTRL_foot jede LINEÁRNĚ dozadu (+Y) rychlostí v = stride / T,
    tj. ve světě (při pohybu postavy dopředu) chodidlo stojí;
  * švihová fáze: chodidlo se vrací dopředu (-Y), zvedá se, pata/špička přes foot roll.
Trup a paže: klíčové pózy contact / down / passing / up (první půlka), druhá půlka zrcadlově.
"""
import sys, math, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import anim_lib as A
from anim_lib import Rig, E


def hermite(p0, p1, m0, m1, u):
    u2, u3 = u * u, u * u * u
    return (2 * u3 - 3 * u2 + 1) * p0 + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * p1 + (u3 - u2) * m1


def foot_state(phi, G):
    """phi: fáze nohy (0 = dopad paty). Vrací (y, z, roll)."""
    s, L = G['stance'], G['L']
    if phi < s:
        t = phi / s
        y = -L / 2 + L * t
        z = 0.0
        if t < G['flat_at']:
            roll = A.lerp(G['strike_roll'], 0.0, A.smooth(t / G['flat_at']) ** 0.8)
        elif t < G['heel_off']:
            roll = 0.0
        else:
            k = (t - G['heel_off']) / (1 - G['heel_off'])
            roll = G['toeoff_roll'] * (k ** 1.6)
        return y, z, roll
    u = (phi - s) / (1 - s)
    m0 = L * (1 - s) / s * G['m0']
    y = hermite(L / 2, -L / 2, m0, m0 * G['m1'], u)
    # zdvih: asymetrický hrb (vrchol v u = lift_peak)
    pk = G['lift_peak']
    if u < pk:
        z = G['lift'] * math.sin(0.5 * math.pi * u / pk) ** 1.2
    else:
        z = G['lift'] * math.cos(0.5 * math.pi * (u - pk) / (1 - pk)) ** 1.6
    if u < G['roll_release']:
        roll = G['toeoff_roll'] * (1 - A.smooth(u / G['roll_release']))
    elif u < G['strike_prep']:
        roll = 0.0
    else:
        roll = G['strike_roll'] * A.smooth((u - G['strike_prep']) / (1 - G['strike_prep']))
    return y, z, roll


def mirror_pose(p):
    q = dict(p)
    hl = list(p['hips_loc']); hl[0] = -hl[0]; q['hips_loc'] = tuple(hl)
    for k in ('hips_rot', 'spine', 'chest', 'neck', 'head'):
        x, y, z = p[k]; q[k] = (x, -y, -z)
    q['armL'], q['armR'] = p['armR'], p['armL']
    return q


def apply_body(rig, p):
    rig.hips(p['hips_loc'], p['hips_rot'])
    for k in ('spine', 'chest', 'neck', 'head'):
        rig.fk(k, p[k])
    rig.arm('.L', **p['armL'])
    rig.arm('.R', **p['armR'])


def build(rig, name, T, G, keyposes):
    ob = rig.ob
    act = A.new_action(ob, name)
    half = T // 2
    frames = sorted(keyposes)
    full = {}
    for f in frames:
        full[f] = keyposes[f]
        full[f + half] = mirror_pose(keyposes[f])
    full[T] = keyposes[0]
    # A) klíčové pózy trupu a paží
    for f in sorted(full):
        rig.reset(); apply_body(rig, full[f])
        rig.key(f, bones=A.FK_BONES + ('CTRL_knee.L', 'CTRL_knee.R', 'CTRL_elbow.L', 'CTRL_elbow.R', 'CTRL_tool'), roll=False)
    # B) chodidla po snímcích (přesná oporová fáze)
    for f in range(T + 1):
        for side, off in (('.R', 0), ('.L', half)):
            phi = ((f - off) % T) / T
            y, z, roll = foot_state(phi, G)
            rig.foot(side, loc=(0, y, z), roll=roll)
        rig.key(f, bones=('CTRL_foot.L', 'CTRL_foot.R'), roll=True, constraints=False)
    A.finalize(act, 0, T, cyclic=True)
    act['in_place'] = True
    act['forward_axis_blender'] = '-Y'
    act['stride_m'] = G['L'] / G['stance']
    act['speed_mps'] = G['L'] / G['stance'] / (T / A.FPS)
    return act


# ------------------------------------------------------------------ WALK
WALK_T = 28
WALK_G = dict(stance=0.60, L=0.336, flat_at=0.14, heel_off=0.55, strike_roll=-0.55, toeoff_roll=0.85,
              lift=0.055, lift_peak=0.42, m0=0.9, m1=0.25, roll_release=0.38, strike_prep=0.62)


def arm(swing, elbow, abd=11, **kw):
    d = dict(swing=swing, elbow=elbow, abd=abd, twist=0.0, fingers=18, thumb=6)
    d.update(kw); return d


WALK_KEYS = {
    0: dict(hips_loc=(0.000, 0, -0.010), hips_rot=(3, 0, 5), spine=(2, 0, -2), chest=(1, 0, -4), neck=(-2, 0, 1), head=(-1, 0, 1),
            armR=arm(-15, 12), armL=arm(19, 27)),
    3: dict(hips_loc=(-0.008, 0, -0.020), hips_rot=(3, 3, 4), spine=(2, -1, -2), chest=(1, -1, -5), neck=(-2, 0, 1), head=(-1, 1, 2),
            armR=arm(-18, 13), armL=arm(22, 30)),
    7: dict(hips_loc=(-0.013, 0, 0.004), hips_rot=(3, 2, 0), spine=(2, -1, 0), chest=(1, -1, 0), neck=(-2, 0, 0), head=(-1, 1, 0),
            armR=arm(-3, 16), armL=arm(5, 20)),
    10: dict(hips_loc=(-0.008, 0, 0.012), hips_rot=(3, 1, -3), spine=(2, 0, 1), chest=(1, 0, 3), neck=(-2, 0, -1), head=(-1, 0, -1),
             armR=arm(10, 22), armL=arm(-9, 13)),
}

# ------------------------------------------------------------------ RUN
RUN_T = 18
RUN_G = dict(stance=0.36, L=0.40, flat_at=0.12, heel_off=0.40, strike_roll=-0.18, toeoff_roll=1.05,
             lift=0.15, lift_peak=0.36, m0=1.5, m1=0.15, roll_release=0.45, strike_prep=0.80)


def rarm(swing, elbow, **kw):
    d = dict(swing=swing, elbow=elbow, abd=17, twist=0.0, fingers=55, thumb=25, wrist=(0, 0, 0))
    d.update(kw); return d


RUN_KEYS = {
    0: dict(hips_loc=(0.000, 0.00, -0.040), hips_rot=(9, 0, 7), spine=(5, 0, -4), chest=(3, 0, -6), neck=(-6, 0, 2), head=(-8, 0, 1),
            armR=rarm(-30, 72), armL=rarm(36, 98)),
    2: dict(hips_loc=(-0.006, 0.00, -0.062), hips_rot=(10, 3, 5), spine=(6, -1, -4), chest=(4, -1, -7), neck=(-7, 0, 2), head=(-8, 1, 2),
            armR=rarm(-36, 70), armL=rarm(40, 102)),
    4: dict(hips_loc=(-0.007, 0.00, -0.032), hips_rot=(9, 2, 1), spine=(5, -1, -1), chest=(3, -1, -1), neck=(-6, 0, 0), head=(-8, 1, 0),
            armR=rarm(-12, 82), armL=rarm(14, 92)),
    6: dict(hips_loc=(-0.003, 0.00, -0.012), hips_rot=(8, 0, -4), spine=(5, 0, 2), chest=(3, 0, 4), neck=(-6, 0, -1), head=(-8, 0, -1),
            armR=rarm(16, 96), armL=rarm(-16, 76)),
}


if __name__ == '__main__':
    src, dst = sys.argv[-2], sys.argv[-1]
    bpy.ops.wm.open_mainfile(filepath=src)
    ob = bpy.data.objects['Traveler_Rig']
    rig = Rig(ob)
    w = build(rig, 'Walk', WALK_T, WALK_G, WALK_KEYS)
    r = build(rig, 'Run', RUN_T, RUN_G, RUN_KEYS)
    for a in (w, r):
        print(a.name, 'stride', round(a['stride_m'], 3), 'speed', round(a['speed_mps'], 3))
    bpy.ops.wm.save_as_mainfile(filepath=dst)
