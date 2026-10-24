"""Authoring vrstva animací: klíčové pózy v sémantických pojmech -> klíče na ovladačích rigu.

Každá akce se skládá z klíčových póz (blocking + breakdowny) na ovladačích a FK kostech.
Interpolaci mezi klíči dělá Blender (Bezier, u smyček cyklické F-modifikátory).
Chodidla v lokomoci se klíčují po snímcích (oporová fáze musí jet přesně lineárně dozadu).
"""
import math, bpy
from mathutils import Vector, Matrix, Quaternion, Euler
import pose_lib as PL

FPS = 30
CTRL_BONES = ('CTRL_foot.L', 'CTRL_foot.R', 'CTRL_knee.L', 'CTRL_knee.R', 'CTRL_elbow.L', 'CTRL_elbow.R', 'CTRL_tool')
FK_BONES = ('root', 'hips', 'spine', 'chest', 'neck', 'head',
            'shoulder.L', 'upper_arm.L', 'forearm.L', 'hand.L', 'fingers.L', 'thumb.L',
            'shoulder.R', 'upper_arm.R', 'forearm.R', 'hand.R', 'fingers.R', 'thumb.R')


def E(x=0.0, y=0.0, z=0.0):
    return PL.eul(x, y, z)


def lerp(a, b, t):
    return a + (b - a) * t


def smooth(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3 - 2 * t)


class Rig:
    def __init__(self, ob):
        self.ob = ob
        self.P = ob.pose.bones
        self._lastq = {}

    # ------------------------------------------------------------- základ
    def reset(self):
        PL.clear(self.ob)
        self.ik_arms(0.0)

    def update(self):
        # POZOR: frame_set by přepsal pózu hodnotami z aktivní akce – při klíčování nevolat.
        bpy.context.view_layer.update()

    def ik_arms(self, infl, sides=('.L', '.R')):
        for s in sides:
            self.P['forearm' + s].constraints['IK'].influence = infl
            self.P['hand' + s].constraints['GripRot'].influence = infl

    # ------------------------------------------------------------- trup
    def hips(self, loc=(0, 0, 0), rot=(0, 0, 0)):
        PL.move_world(self.P['hips'], loc)
        PL.rot_world(self.P['hips'], E(*rot))

    def fk(self, bone, rot):
        PL.rot_world(self.P[bone], E(*rot))

    # ------------------------------------------------------------- paže (FK)
    def arm(self, side, swing=0.0, abd=10.0, twist=0.0, elbow=10.0, wrist=(0, 0, 0), clav=(0, 0, 0),
            fingers=10.0, thumb=0.0, fwd_bias=0.0):
        """swing: + dopředu (stupně, sagitální rovina), abd: odtažení od těla do strany,
        elbow: flexe lokte (+ dopředu), wrist: rotace zápěstí (x,y,z), clav: rotace klíční kosti."""
        sgn = 1.0 if side == '.L' else -1.0
        a = math.radians(abd); w = math.radians(swing)
        # dvoukrokově: nejdřív odtažení v čelní rovině (bez twistu), pak švih v sagitální rovině
        # -> osa ohybu lokte zůstává ~kolmá na sagitální rovinu, předloktí nemíří do trupu
        ub = self.P['upper_arm' + side].bone
        d0 = (ub.tail_local - ub.head_local).normalized()
        d_abd = Vector((math.sin(a) * sgn, 0.0, -math.cos(a)))
        W = Matrix.Rotation(-w, 3, 'X') @ d0.rotation_difference(d_abd).to_matrix()
        if twist:
            W = Quaternion(W @ d0, math.radians(twist * sgn)).to_matrix() @ W
        PL.rot_world(self.P['shoulder' + side], E(clav[0], clav[1] * sgn, clav[2] * sgn))
        PL.rot_world(self.P['upper_arm' + side], W)
        PL.local_x(self.P['forearm' + side], elbow)
        PL.rot_world(self.P['hand' + side], E(wrist[0], wrist[1] * sgn, wrist[2] * sgn))
        self.grip(side, fingers, thumb)

    def grip(self, side, fingers, thumb=0.0):
        PL.local_x(self.P['fingers' + side], fingers)
        PL.local_x(self.P['thumb' + side], thumb)

    # ------------------------------------------------------------- nohy (IK)
    def foot(self, side, loc=(0, 0, 0), pitch=0.0, yaw=0.0, roll=0.0, bank=0.0):
        """CTRL_foot: posun z rest pozice (pata na zemi), rotace kolem paty (pitch + = špička nahoru)."""
        pb = self.P['CTRL_foot' + side]
        C0 = pb.bone.matrix_local
        h = C0.translation
        R = (Matrix.Rotation(math.radians(yaw), 3, 'Z') @ Matrix.Rotation(math.radians(pitch), 3, 'X')
             @ Matrix.Rotation(math.radians(bank), 3, 'Y')).to_4x4()
        M = Matrix.Translation(Vector(loc)) @ Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ C0
        pb.matrix = M
        pb['roll'] = roll

    def foot_world(self, side, foot_matrix):
        """Nastav CTRL_foot tak, aby deformační kost foot měla danou matici (roll=0)."""
        pb = self.P['CTRL_foot' + side]
        C0 = pb.bone.matrix_local
        F0 = self.ob.data.bones['foot' + side].matrix_local
        pb.matrix = foot_matrix @ (C0.inverted() @ F0).inverted()
        pb['roll'] = 0.0

    def knee_pole(self, side, loc_world_delta):
        pb = self.P['CTRL_knee' + side]
        pb.location = PL.R_rest(pb).inverted() @ Vector(loc_world_delta)

    def elbow_pole(self, side, pos):
        pb = self.P['CTRL_elbow' + side]
        M = pb.bone.matrix_local.copy(); M.translation = Vector(pos)
        pb.matrix = M

    # ------------------------------------------------------------- nástroj
    def tool(self, M):
        self.P['CTRL_tool'].matrix = M

    # ------------------------------------------------------------- klíčování
    def key(self, frame, bones=None, roll=True, constraints=True):
        bones = bones or (FK_BONES + CTRL_BONES)
        for n in bones:
            pb = self.P[n]
            q = pb.rotation_quaternion.copy()
            last = self._lastq.get(n)
            if last is not None and last.dot(q) < 0:
                q.negate(); pb.rotation_quaternion = q
            self._lastq[n] = q.copy()
            pb.keyframe_insert('rotation_quaternion', frame=frame, group=n)
            pb.keyframe_insert('location', frame=frame, group=n)
        if roll:
            for s in ('.L', '.R'):
                self.P['CTRL_foot' + s].keyframe_insert('["roll"]', frame=frame, group='CTRL_foot' + s)
        if constraints:
            for s in ('.L', '.R'):
                self.P['forearm' + s].constraints['IK'].keyframe_insert('influence', frame=frame)
                self.P['hand' + s].constraints['GripRot'].keyframe_insert('influence', frame=frame)


def new_action(ob, name):
    if ob.animation_data is None:
        ob.animation_data_create()
    act = bpy.data.actions.get(name)
    if act:
        bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    ob.animation_data.action = act
    return act


def fcurves_of(act, ob=None):
    out = []
    for layer in act.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                out.extend(cb.fcurves)
    return out


def finalize(act, start, end, cyclic=False, linear_paths=()):
    act.use_frame_range = True
    act.frame_start = start; act.frame_end = end
    act.use_cyclic = cyclic
    for fc in fcurves_of(act):
        for kp in fc.keyframe_points:
            kp.interpolation = 'BEZIER'
            kp.handle_left_type = kp.handle_right_type = 'AUTO_CLAMPED'
        if any(p in fc.data_path for p in linear_paths):
            for kp in fc.keyframe_points:
                kp.interpolation = 'LINEAR'
        if cyclic and not any(m.type == 'CYCLES' for m in fc.modifiers):
            fc.modifiers.new('CYCLES')
        fc.update()


def bake_deform(ob, act, frames):
    """Vrátí {frame: {bone: Matrix(local basis)}} – vizuální transformace deform kostí (vč. constraintů)."""
    ob.animation_data.action = act
    sc = bpy.context.scene
    res = {}
    deform = [pb for pb in ob.pose.bones if pb.bone.use_deform or pb.name == 'root']
    for f in frames:
        sc.frame_set(int(f), subframe=f - int(f))
        d = {}
        for pb in deform:
            d[pb.name] = ob.convert_space(pose_bone=pb, matrix=pb.matrix, from_space='POSE', to_space='LOCAL')
        res[f] = d
    return res


def fk_matrix(ob, name, cache=None):
    """Pozice kosti z aktuálních hodnot loc/rot (bez vyhodnocení depsgraphu a constraintů)."""
    cache = {} if cache is None else cache
    if name in cache:
        return cache[name]
    pb = ob.pose.bones[name]; b = pb.bone
    if b.parent is None:
        M = b.matrix_local @ pb.matrix_basis
    else:
        M = fk_matrix(ob, b.parent.name, cache) @ (b.parent.matrix_local.inverted() @ b.matrix_local) @ pb.matrix_basis
    cache[name] = M
    return M


def solve_reach(rig, tool_M, sides=('.L', '.R'), margin=0.985, iters=12, gain=0.7, max_shift=0.06):
    """Pokud je grip mimo dosah paže, posuň boky (a tím rameno) směrem k cíli. Vrací zbytkovou chybu."""
    ob = rig.ob
    T0 = ob.data.bones['CTRL_tool'].matrix_local
    res = 0.0
    hp0 = PL.R_rest(rig.P['hips']) @ Vector(rig.P['hips'].location)
    for _ in range(iters):
        shift = Vector((0, 0, 0)); res = 0.0
        cache = {}
        for s in sides:
            G = tool_M @ (T0.inverted() @ ob.data.bones['MCH_grip' + s].matrix_local)
            sh = fk_matrix(ob, 'upper_arm' + s, cache).translation
            L = (ob.data.bones['upper_arm' + s].length + ob.data.bones['forearm' + s].length) * margin
            d = G.translation - sh
            if d.length > L:
                ex = d.length - L
                res = max(res, ex)
                shift += d.normalized() * ex
        if res < 1e-4:
            break
        hp = rig.P['hips']
        cur = PL.R_rest(hp) @ Vector(hp.location)
        new = cur + shift * gain
        dlt = new - hp0
        if dlt.length > max_shift:
            new = hp0 + dlt.normalized() * max_shift
        PL.move_world(hp, new)
    return res


# ---------------------------------------------------------------- kolizní řešič klíčových póz
import numpy as _np
import body_sdf as _S

_TORSO = [('chest', _S.P['chest_c'], _S.P['chest_r']), ('spine', _S.P['belly_c'], _S.P['belly_r']),
          ('hips', _S.P['pelvis_c'], _S.P['pelvis_r']), ('head', _S.P['head_c'], _S.P['head_r'])]
HAT_CROWN_C = Vector((0.0, 0.0, 0.11)); HAT_CROWN_R = Vector((0.215, 0.215, 0.20))
HAT_BRIM_R = 0.36; HAT_BRIM_Z = (0.02, 0.10)


def _ell(p, c, r):
    q = Vector(((p.x - c[0]) / r[0], (p.y - c[1]) / r[1], (p.z - c[2]) / r[2]))
    k = q.length
    d = (k - 1.0) * min(r)
    g = Vector((q.x / r[0], q.y / r[1], q.z / r[2])).normalized() if k > 1e-6 else Vector((0, 0, 1))
    return d, g


class Solver:
    """Kopie rigu bez animace: vyhodnotí IK/constrainty pro aktuální (neklíčovanou) pózu."""

    def __init__(self, ob, tool_obj=None):
        self.ob = ob
        s = ob.copy(); s.animation_data_clear(); s.name = '_SOLVER'
        bpy.context.scene.collection.objects.link(s)
        for pb in s.pose.bones:
            for c in pb.constraints:
                if getattr(c, 'target', None) == ob:
                    c.target = s
                if getattr(c, 'pole_target', None) == ob:
                    c.pole_target = s
        self.s = s
        self.tool_pts = None
        if tool_obj is not None:
            self.tool_pts = [v.co.copy() for v in tool_obj.data.vertices][::3]

    def remove(self):
        bpy.data.objects.remove(self.s)

    def evaluate(self, rig):
        src = rig.P
        saved = {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy(), pb.get('roll')) for pb in src}
        infl = {(pb.name, c.name): c.influence for pb in src for c in pb.constraints}
        for pb in self.s.pose.bones:
            l, q, r = saved[pb.name]
            pb.location = l; pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = q
            for c in pb.constraints:
                c.influence = infl[(pb.name, c.name)]
        bpy.context.view_layer.update()
        # obnov pózu rigu (update mohl přepsat hodnoty aktivní akcí)
        for pb in src:
            l, q, r = saved[pb.name]
            pb.location = l; pb.rotation_quaternion = q
            if r is not None:
                pb['roll'] = r
        for pb in src:
            for c in pb.constraints:
                c.influence = infl[(pb.name, c.name)]
        return self.s.pose.bones

    def _to_rest(self, P, bn, p):
        M = P[bn].matrix @ self.ob.data.bones[bn].matrix_local.inverted()
        return M, M.inverted() @ p

    def arm_penetration(self, P, sides=('.L', '.R')):
        """Nejhlubší průnik předloktí/ruky do elipsoidů trupu; vrací (hloubka, směr ven ve světě)."""
        worst = (0.0, Vector((0, -1, 0)))
        for s in sides:
            pts = []
            for bn, rad in (('forearm' + s, 0.050), ('hand' + s, 0.060)):
                h, t = P[bn].head, P[bn].tail
                for k in range(4):
                    pts.append((h.lerp(t, k / 3), rad))
            pts.append((P['fingers' + s].tail, 0.045))
            for p, rad in pts:
                for bn, c, r in _TORSO:
                    M, lp = self._to_rest(P, bn, p)
                    d, g = _ell(lp, c, r)
                    pen = rad - d
                    if pen > worst[0]:
                        worst = (pen, (M.to_3x3() @ g).normalized())
                        self.last_arm = (s, round(p.x, 2), round(p.y, 2), round(p.z, 2), bn)
        return worst

    def tool_penetration(self, P, Mt, hat=True):
        worst = (0.0, Vector((0, -1, 0)))
        if not self.tool_pts:
            return worst
        Mh, _ = self._to_rest(P, 'head', Vector())
        Mhi = Mh.inverted()
        hc = Vector(_S.P['head_c'])
        for v in self.tool_pts:
            p = Mt @ v
            for bn, c, r in _TORSO:
                M, lp = self._to_rest(P, bn, p)
                d, g = _ell(lp, c, r)
                pen = 0.02 - d
                if pen > worst[0]:
                    worst = (pen, (M.to_3x3() @ g).normalized())
            if hat:
                lp = Mhi @ p - hc
                d, g = _ell(lp, HAT_CROWN_C, HAT_CROWN_R)
                pen = 0.03 - d
                if pen > worst[0]:
                    worst = (pen, (Mh.to_3x3() @ g).normalized())
                if HAT_BRIM_Z[0] - 0.03 < lp.z < HAT_BRIM_Z[1] + 0.03:
                    rad = Vector((lp.x, lp.y)).length
                    pen = HAT_BRIM_R + 0.03 - rad
                    if pen > worst[0]:
                        g = Vector((lp.x, lp.y, 0)).normalized()
                        worst = (pen, (Mh.to_3x3() @ g).normalized())
        return worst


def solve_clearance(rig, solver, Mt, iters=10, tool_hat=True, verbose=False):
    """Posouvá nástroj (a s ním gripy) ven z trupu/hlavy/klobouku, dokud paže i nástroj nemají průnik."""
    Mt = Mt.copy()
    for it in range(iters):
        rig.tool(Mt)
        solve_reach(rig, Mt)
        P = solver.evaluate(rig)
        pa, da = solver.arm_penetration(P)
        pt, dt = solver.tool_penetration(P, Mt, tool_hat)
        if verbose:
            print('   clear it', it, round(pa, 3), round(pt, 3), getattr(solver, 'last_arm', None))
        if pa < 0.004 and pt < 0.004:
            break
        move = Vector((0, 0, 0))
        if pa >= 0.004:
            move += da * (pa + 0.01)
        if pt >= 0.004:
            move += dt * (pt + 0.01)
        Mt.translation += move
    rig.tool(Mt)
    solve_reach(rig, Mt)
    return Mt
