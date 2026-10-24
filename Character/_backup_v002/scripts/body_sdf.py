"""Analytická SDF definice bílého těla Everlost cestovatele.

Souřadnice Blenderu: Z nahoru, postava hledí do -Y, levá strana postavy = +X.
Jednotky: metry. Výška bez klobouku ~1.52 m. Póza: A-pose (paže ~42° pod horizontálou).
Tvar se definuje hladkým sjednocením (smooth-min) měkkých primitiv, aby výsledný
povrch byl jediná souvislá plocha bez viditelných průniků koulí/válců.
Tento soubor je jediný zdroj pravdy pro proporce; mesh vzniká projekcí čisté
quad sítě na tuto plochu (viz build_body.py).
"""
import numpy as np

# ------------------------------------------------------------------ proporce
P = dict(
    head_c=np.array([0.0, 0.0, 1.278]), head_r=np.array([0.208, 0.200, 0.212]),
    neck_a=np.array([0.0, 0.008, 1.010]), neck_b=np.array([0.0, 0.004, 1.120]), neck_r=0.072,
    chest_c=np.array([0.0, 0.004, 0.890]), chest_r=np.array([0.192, 0.136, 0.142]),
    belly_c=np.array([0.0, -0.010, 0.755]), belly_r=np.array([0.182, 0.142, 0.128]),
    pelvis_c=np.array([0.0, 0.010, 0.640]), pelvis_r=np.array([0.178, 0.128, 0.104]),
    shoulder=np.array([0.212, 0.008, 0.952]),
    elbow=np.array([0.350, 0.022, 0.812]),
    wrist=np.array([0.467, 0.000, 0.692]),
    hip=np.array([0.100, 0.008, 0.585]),
    knee=np.array([0.106, -0.006, 0.325]),
    ankle=np.array([0.106, 0.010, 0.095]),
)
R_UPPERARM = (0.072, 0.058)
R_FOREARM = (0.058, 0.052)
R_THIGH = (0.108, 0.080)
R_SHIN = (0.080, 0.062)


def _len(v):
    return np.sqrt(np.sum(v * v, axis=-1))


def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def ellipsoid(p, c, r):
    q = (p - c) / r
    k0 = _len(q)
    k1 = _len(q / r)
    return k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)


def round_cone(p, a, b, ra, rb):
    """Kužel se zaoblenými konci (vzdálenost mezi koulemi a,b s poloměry ra, rb)."""
    ba = b - a
    l2 = np.dot(ba, ba)
    t = np.clip(np.sum((p - a) * ba, axis=-1) / l2, 0.0, 1.0)
    proj = a + t[..., None] * ba
    r = ra + (rb - ra) * t
    return _len(p - proj) - r


def rot_frame(fwd, up_hint):
    f = fwd / np.linalg.norm(fwd)
    s = np.cross(f, up_hint); s /= np.linalg.norm(s)
    u = np.cross(s, f)
    return f, s, u


def mitten(p, side):
    """Měkká palčáková ruka: dlaň + prsty v jednom celku, oddělený palec."""
    wr = P['wrist'] * np.array([side, 1, 1])
    el = P['elbow'] * np.array([side, 1, 1])
    f, s, u = rot_frame(wr - el, np.array([0, -1.0, 0]))  # u ~ směr palce (dopředu)
    # lokální souřadnice: f podél ruky, s = šířka dlaně (normála dlaně), u = dopředu
    d = p - wr
    lf = np.sum(d * f, -1); ls = np.sum(d * s, -1); lu = np.sum(d * u, -1)
    q = np.stack([lf, lu, ls], -1)  # (podél, šířka dlaně, tloušťka)
    palm = ellipsoid(q, np.array([0.070, 0.0, 0.0]), np.array([0.080, 0.074, 0.062]))
    fing = ellipsoid(q, np.array([0.122, -0.006, 0.006]), np.array([0.062, 0.072, 0.060]))
    hand = smin(palm, fing, 0.03)
    th_a = np.array([0.040, 0.052, 0.010]); th_b = np.array([0.092, 0.084, 0.016])
    thumb = round_cone(q, th_a, th_b, 0.031, 0.028)
    hand = smin(hand, thumb, 0.012)
    wrist_link = round_cone(q, np.array([-0.04, 0, 0]), np.array([0.03, 0, 0]), 0.051, 0.050)
    return smin(hand, wrist_link, 0.02)


def foot(p, side):
    an = P['ankle'] * np.array([side, 1, 1])
    q = p - an
    body = ellipsoid(q, np.array([0.0, -0.045, -0.040]), np.array([0.058, 0.102, 0.056]))
    heel = ellipsoid(q, np.array([0.0, 0.022, -0.045]), np.array([0.054, 0.052, 0.050]))
    f = smin(body, heel, 0.03)
    # plochá podrážka: odřízni pod podlahou
    sole = -(p[..., 2] - 0.0)
    f = -smin(-f, -sole, 0.012)  # měkký průnik s poloprostorem z>=0 (zaoblená hrana podrážky)
    return f


def body_sdf(p):
    p = np.asarray(p, dtype=np.float64)
    pm = p.copy(); pm[..., 0] = np.abs(pm[..., 0])  # symetrie
    d = ellipsoid(p, P['head_c'], P['head_r'])
    neck = round_cone(p, P['neck_a'], P['neck_b'], P['neck_r'] + 0.006, P['neck_r'])
    d = smin(d, neck, 0.035)
    torso = ellipsoid(p, P['chest_c'], P['chest_r'])
    torso = smin(torso, ellipsoid(p, P['belly_c'], P['belly_r']), 0.11)
    torso = smin(torso, ellipsoid(p, P['pelvis_c'], P['pelvis_r']), 0.09)
    # trapéz / ramenní oblouk
    trap = round_cone(pm, np.array([0.03, 0.01, 1.005]), P['shoulder'] + np.array([-0.02, 0, 0.012]), 0.05, 0.058)
    torso = smin(torso, trap, 0.04)
    d = smin(d, torso, 0.045)
    arm = round_cone(pm, P['shoulder'], P['elbow'], *R_UPPERARM)
    arm = smin(arm, round_cone(pm, P['elbow'], P['wrist'], *R_FOREARM), 0.008)
    arm = smin(arm, mitten(pm, 1.0), 0.015)
    d = smin(d, arm, 0.035)
    leg = round_cone(pm, P['hip'], P['knee'], *R_THIGH)
    leg = smin(leg, round_cone(pm, P['knee'], P['ankle'], *R_SHIN), 0.008)
    leg = smin(leg, foot(pm, 1.0), 0.03)
    d = smin(d, leg, 0.03)
    return d


def grad(p, e=1e-4):
    g = np.zeros_like(p)
    for i in range(3):
        dp = np.zeros(3); dp[i] = e
        g[..., i] = (body_sdf(p + dp) - body_sdf(p - dp)) / (2 * e)
    return g


def project(p, iters=6):
    for _ in range(iters):
        f = body_sdf(p)
        g = grad(p)
        p = p - (f / np.maximum(np.sum(g * g, -1), 1e-9))[..., None] * g
    return p
