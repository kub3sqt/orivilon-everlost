"""Číselná kontrola celé akce (každý snímek + mezisnímky):
 - kontakt dlaní s gripy (IK chyba, mm),
 - průnik nástroje do hlavy a do klobouku (proxy s rezervou), do těla,
 - průnik paží (předloktí/ruka) do trupu,
 - u Chop: ostří vs kmen (zakousnutí), u Mine: hrot vs zem.
Použití: python3 check_action.py -- file.blend Action [sub=4] [json_out]
"""
import sys, math, json, bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import body_sdf as S, tools_lib as T

args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0])
act_name = args[1]
sub = int(next((a.split('=')[1] for a in args if a.startswith('sub=')), 4))
jout = next((a for a in args if a.endswith('.json')), None)
ob = bpy.data.objects['Traveler_Rig']; body = bpy.data.objects['Body']
act = bpy.data.actions[act_name]; ob.animation_data.action = act
tool_name = act.get('tool')
tool = bpy.data.objects.get('Tool_' + tool_name) if tool_name else None
sc = bpy.context.scene
P = ob.pose.bones
HEAD_C = S.P['head_c']; HEAD_R = S.P['head_r']
# klobouk (rezerva pro etapu 3) v prostoru kosti hlavy: koruna + krempa
HAT_CROWN_C = np.array([0.0, 0.0, 0.11]); HAT_CROWN_R = np.array([0.215, 0.215, 0.20])
HAT_BRIM_R = 0.36; HAT_BRIM_Z = (0.02, 0.10)  # relativně ke středu hlavy, krempa mírně spadá

# dominantní skupiny vrcholů těla
groups = {g.index: g.name for g in body.vertex_groups}
dom = []
for v in body.data.vertices:
    if v.groups:
        g = max(v.groups, key=lambda x: x.weight); dom.append(groups[g.group])
    else:
        dom.append('')
ARM = {'.L': [i for i, d in enumerate(dom) if d in ('forearm.L', 'hand.L', 'fingers.L', 'thumb.L')],
       '.R': [i for i, d in enumerate(dom) if d in ('forearm.R', 'hand.R', 'fingers.R', 'thumb.R')]}
NOHAND_V = set(i for i, d in enumerate(dom) if not d.startswith(('hand', 'fingers', 'thumb')))
TORSO_V = set(i for i, d in enumerate(dom) if d in ('hips', 'spine', 'chest', 'neck', 'head'))


def posed_body():
    dg = bpy.context.evaluated_depsgraph_get()
    be = body.evaluated_get(dg)
    me = be.to_mesh()
    co = np.array([(be.matrix_world @ v.co)[:] for v in me.vertices])
    polys = [tuple(p.vertices) for p in me.polygons]
    be.to_mesh_clear()
    return co, polys


TORSO_PARTS = [('chest', S.P['chest_c'], S.P['chest_r']), ('spine', S.P['belly_c'], S.P['belly_r']),
               ('hips', S.P['pelvis_c'], S.P['pelvis_r']), ('head', S.P['head_c'], S.P['head_r'])]


WHERE = {}


def torso_depth(pts, shrink=0.012, tag=None):
    """Hloubka průniku bodů do analytických elipsoidů trupu/hlavy (v rest prostoru příslušné kosti)."""
    worst = 0.0
    for bn, c, r in TORSO_PARTS:
        M = ob.matrix_world @ P[bn].matrix @ ob.data.bones[bn].matrix_local.inverted()
        Mi = M.inverted()
        loc = np.array([(Mi @ Vector(p))[:] for p in pts])
        rr = r - shrink
        q = (loc - c) / rr
        d = (np.sqrt(np.sum(q * q, 1)) - 1.0) * rr.min()
        if float(-d.min()) > worst:
            worst = float(-d.min())
            if tag:
                WHERE[tag] = (bn, int(np.argmin(d)))
    return worst


def inside_depth(bvh, pts):
    worst = 0.0
    for p in pts:
        hit = bvh.find_nearest(Vector(p), 0.08)
        if hit[0] is None:
            continue
        loc, nrm, idx, dist = hit
        if (Vector(p) - loc).dot(nrm) < 0:
            worst = max(worst, dist)
    return worst


f0, f1 = int(act.frame_start), int(act.frame_end)
rep = dict(action=act_name, frames=[])
tool_local = None
if tool:
    tool_local = np.array([v.co[:] for v in tool.data.vertices])
for f in range(f0, f1):
    for k in range(sub):
        t = f + k / sub
        sc.frame_set(f, subframe=k / sub)
        r = {'t': round(t, 3)}
        co, polys = posed_body()
        # IK grip chyba
        if P['forearm.R'].constraints['IK'].influence > 0.5:
            r['grip_err_R_mm'] = round((P['hand.R'].head - P['MCH_grip.R'].head).length * 1000, 2)
            r['grip_err_L_mm'] = round((P['hand.L'].head - P['MCH_grip.L'].head).length * 1000, 2)
            # skutečná geometrie: leží osa násady v místě úchopu UVNITŘ sevřené pěsti?
            bvh_all = BVHTree.FromPolygons([Vector(c) for c in co], polys)
            Mt = ob.matrix_world @ P['CTRL_tool'].matrix
            import rig_lib as _R
            for side, gy in (('R', 0.0), ('L', _R.TOOL_GRIP_L_Y)):
                inside = 0; tot = 0
                for dy in (-0.025, -0.0125, 0.0, 0.0125, 0.025):
                    q = Mt @ Vector((0, gy + dy, 0)); tot += 1
                    hits = 0; o = q.copy(); dvec = Vector((0.0, 0.0, 1.0))
                    for _ in range(20):
                        h = bvh_all.ray_cast(o, dvec)
                        if h[0] is None: break
                        hits += 1; o = h[0] + dvec * 1e-4
                    inside += hits % 2
                r[f'fist_enclose_{side}'] = round(inside / tot, 2)
        for side in ('L', 'R'):
            fa = (P['forearm.' + side].tail - P['forearm.' + side].head).normalized()
            hy = (P['hand.' + side].tail - P['hand.' + side].head).normalized()
            r[f'wrist_bend_{side}_deg'] = round(math.degrees(fa.angle(hy)), 1)
            # přetočení zápěstí: swing-twist rozklad (ruka vs předloktí, relativně k rest)
            import grip_solver as GS
            Fp = (ob.matrix_world @ P['forearm.' + side].matrix).to_3x3(); Hp = (ob.matrix_world @ P['hand.' + side].matrix).to_3x3()
            F0 = ob.data.bones['forearm.' + side].matrix_local.to_3x3(); H0 = ob.data.bones['hand.' + side].matrix_local.to_3x3()
            qd = ((Fp.inverted() @ Hp) @ (F0.inverted() @ H0).inverted()).to_quaternion()
            t = math.degrees(2 * math.atan2(qd.y, qd.w))
            r[f'wrist_twistgeo_{side}_deg'] = round(GS.forearm_hand_twist(ob, '.' + side, P['upper_arm.' + side].head, P['forearm.' + side].head, P['hand.' + side].head, Hp) or 0.0, 1)
            r[f'wrist_twist_{side}_deg'] = round((t + 180) % 360 - 180, 1)
        # paže vs trup
        dl = torso_depth(co[ARM['.L']], tag='L'); dr = torso_depth(co[ARM['.R']], tag='R')
        r['armL_in_torso_mm'] = round(dl * 1000, 1); r['armR_in_torso_mm'] = round(dr * 1000, 1)
        if max(dl, dr) > 0.015:
            s_ = 'L' if dl > dr else 'R'; bn, ix = WHERE[s_]; r['arm_hit'] = f"{s_}:{dom[ARM['.' + s_][ix]]}->{bn}"
        if tool:
            Mw = tool.matrix_world
            tv = np.array([(Mw @ Vector(v))[:] for v in tool_local])
            Hm = ob.matrix_world @ P['head'].matrix
            Hinv = Hm.inverted()
            hc_rest = Vector(HEAD_C) - ob.data.bones['head'].head_local
            loc = np.array([(Hinv @ Vector(v))[:] for v in tv])
            # do prostoru "osy kosti hlavy" -> převod na rest-world orientaci kosti hlavy
            R0 = ob.data.bones['head'].matrix_local.to_3x3()
            loc = np.array([(R0 @ Vector(v))[:] for v in loc]) - np.array(hc_rest[:])
            dh = (np.sqrt(np.sum((loc / HEAD_R) ** 2, 1)) - 1.0) * HEAD_R.min()
            r['tool_head_clear_mm'] = round(float(dh.min()) * 1000, 1)
            dc = (np.sqrt(np.sum(((loc - HAT_CROWN_C) / HAT_CROWN_R) ** 2, 1)) - 1.0) * HAT_CROWN_R.min()
            rad = np.sqrt(loc[:, 0] ** 2 + loc[:, 1] ** 2)
            in_brim = (rad < HAT_BRIM_R) & (loc[:, 2] > HAT_BRIM_Z[0]) & (loc[:, 2] < HAT_BRIM_Z[1])
            brim_clear = np.where((loc[:, 2] > HAT_BRIM_Z[0]) & (loc[:, 2] < HAT_BRIM_Z[1]), rad - HAT_BRIM_R, 1.0)
            r['tool_hat_clear_mm'] = round(float(min(dc.min(), brim_clear.min())) * 1000, 1)
            r['tool_in_torso_mm'] = round(torso_depth(tv[::2], 0.0) * 1000, 1)
            hat = bpy.data.objects.get('Outfit_Hat')
            if hat:
                dg = bpy.context.evaluated_depsgraph_get(); he = hat.evaluated_get(dg); hm = he.to_mesh()
                bvh_h = BVHTree.FromPolygons([he.matrix_world @ v.co for v in hm.vertices], [tuple(p.vertices) for p in hm.polygons])
                he.to_mesh_clear()
                dmin = min(bvh_h.find_nearest(Vector(p))[3] for p in tv[::2])
                r['tool_to_real_hat_mm'] = round(dmin * 1000, 1)
            if tool_name == 'Axe':
                e = np.array((Mw @ Vector((0.133, 0.40, 0)))[:])
                d = math.hypot(e[0] - T.TRUNK_C.x, e[1] - T.TRUNK_C.y) - T.TRUNK_R
                r['edge_to_trunk_mm'] = round(d * 1000, 1)
                dd = np.hypot(tv[:, 0] - T.TRUNK_C.x, tv[:, 1] - T.TRUNK_C.y) - T.TRUNK_R
                r['axe_into_trunk_mm'] = round(float(-dd.min()) * 1000, 1)
                r['edge_err_contact_mm'] = round((Vector(e) - T.CHOP_CONTACT).length * 1000, 1)
            else:
                tip = Mw @ Vector((0.185, 0.465, 0))
                r['tip_z_mm'] = round(tip.z * 1000, 1)
                r['tool_min_z_mm'] = round(float(tv[:, 2].min()) * 1000, 1)
                r['tip_err_target_mm'] = round((tip - T.MINE_TARGET).length * 1000, 1)
        rep['frames'].append(r)
# shrnutí
keys = set(k for r in rep['frames'] for k in r if k != 't')
summ = {}
for k in sorted(keys):
    vals = [(r[k], r['t']) for r in rep['frames'] if k in r]
    if 'clear' in k or k.startswith('fist_') or k in ('edge_to_trunk_mm', 'tip_z_mm', 'tool_min_z_mm', 'tool_to_real_hat_mm'):
        v, t = min(vals); summ[k] = f'min {v} @ {t}'
    elif 'twist' in k:
        v, t = max((abs(a), b) for a, b in vals); summ[k] = f'max|.| {v} @ {t}'
    elif False:
        v, t = min(vals); summ[k] = f'min {v} @ {t}'
    else:
        v, t = max(vals); summ[k] = f'max {v} @ {t}'
rep['summary'] = summ
print(json.dumps(summ, indent=1, ensure_ascii=False))
if jout:
    json.dump(rep, open(jout, 'w'), indent=1, ensure_ascii=False)
