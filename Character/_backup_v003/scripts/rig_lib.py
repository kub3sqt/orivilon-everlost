"""Rig Everlost cestovatele: deformační kostra (export) + ovládací vrstva (authoring).

Konvence:
 * Blender: Z nahoru, postava hledí do -Y, levá strana postavy = +X (.L).
 * Deformační kosti (use_deform=True) = jediné kosti, které jdou do FBX/GLB.
 * CTRL_* = animátorské ovladače, MCH_* = mechanika (foot roll, gripy). Do exportu nejdou.
 * Ruce: IK/FK přepínač = influence IK a CopyRotation na forearm/hand (klíčuje se v akci).
 * Nohy: vždy IK s reverse-foot (vlastnost 'roll' na CTRL_foot.*), póly kolen v CTRL_knee.*.
 * Nástroj: CTRL_tool nese nástroj; MCH_grip_R/L jsou cílové pozice dlaní na násadě.
   Deformační kost prop.R (dítě hand.R) nese nástroj v exportu (konstantní offset).
"""
import sys, math, numpy as np, bpy
from mathutils import Vector, Matrix, Quaternion
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import body_sdf as S

P = S.P
V = lambda a: Vector(tuple(float(x) for x in a))

# lokální offset osy násady v prostoru kosti ruky (osa Y = podél dlaně, Z = normála dlaně)
GRIP_LOCAL = Vector((0.0, 0.090, 0.035))  # v002: násada prochází středem sevřené pěsti
TOOL_GRIP_L_Y = -0.160  # poloha levé (spodní) ruky na násadě v prostoru nástroje


def mirror(v, side):
    return Vector((v[0] * side, v[1], v[2]))


def hand_frame(side):
    """Vrátí (f, u, s) – podél ruky, směr palce, normála dlaně – ve world (rest)."""
    m = np.array([side, 1, 1])
    wr, el = P['wrist'] * m, P['elbow'] * m
    f, s, u = S.rot_frame(wr - el, np.array([0, -1.0, 0]))
    if side < 0:
        # rot_frame na pravé straně počítáme zrcadlením levé (geometrie SDF je zrcadlená)
        fl, sl, ul = S.rot_frame(P['wrist'] - P['elbow'], np.array([0, -1.0, 0]))
        f, s, u = fl * m, sl * m, ul * m
    return V(f), V(u), V(s)


def bone_defs():
    """(name, head, tail, parent, deform, roll_z_ref or None, connected)"""
    D = []
    add = lambda *a: D.append(a)
    add('root', (0, 0, 0), (0, 0.22, 0), None, False, (0, 0, 1), False)
    add('hips', (0, 0.010, 0.620), (0, 0.008, 0.720), 'root', True, (0, -1, 0), False)
    add('spine', (0, 0.008, 0.720), (0, 0.004, 0.845), 'hips', True, (0, -1, 0), True)
    add('chest', (0, 0.004, 0.845), (0, 0.008, 1.010), 'spine', True, (0, -1, 0), True)
    add('neck', (0, 0.008, 1.010), (0, 0.004, 1.105), 'chest', True, (0, -1, 0), True)
    add('head', (0, 0.004, 1.105), (0, 0.0, 1.470), 'neck', True, (0, -1, 0), True)
    for side, sfx in ((1, '.L'), (-1, '.R')):
        sh, el, wr = mirror(P['shoulder'], side), mirror(P['elbow'], side), mirror(P['wrist'], side)
        f, u, s = hand_frame(side)
        # osy paže: Z kosti míří dopředu-dolů (kolmo na rovinu ohybu lokte)
        add('shoulder' + sfx, (0.040 * side, 0.010, 0.968), tuple(sh), 'chest', True, (0, -1, 0), False)
        add('upper_arm' + sfx, tuple(sh), tuple(el), 'shoulder' + sfx, True, (0, -1, 0), True)
        add('forearm' + sfx, tuple(el), tuple(wr), 'upper_arm' + sfx, True, (0, -1, 0), True)
        add('hand' + sfx, tuple(wr), tuple(wr + f * 0.092), 'forearm' + sfx, True, tuple(s), True)
        add('fingers' + sfx, tuple(wr + f * 0.092), tuple(wr + f * 0.168), 'hand' + sfx, True, tuple(s), True)
        add('thumb' + sfx, tuple(wr + f * 0.030 + u * 0.040), tuple(wr + f * 0.090 + u * 0.082 + s * 0.012), 'hand' + sfx, True, tuple(s), False)
        hp, kn, an = mirror(P['hip'], side), mirror(P['knee'], side), mirror(P['ankle'], side)
        ball = an + Vector((0, -0.088, -0.060))
        tip = an + Vector((0, -0.140, -0.065))
        add('thigh' + sfx, tuple(hp), tuple(kn), 'hips', True, (0, -1, 0), False)
        add('shin' + sfx, tuple(kn), tuple(an), 'thigh' + sfx, True, (0, -1, 0), True)
        add('foot' + sfx, tuple(an), tuple(ball), 'shin' + sfx, True, (0, 0, 1), True)
        add('toe' + sfx, tuple(ball), tuple(tip), 'foot' + sfx, True, (0, 0, 1), True)
    return D


def ensure_mode(ob, mode):
    bpy.context.view_layer.objects.active = ob
    if ob.mode != mode:
        bpy.ops.object.mode_set(mode=mode)


def build_armature(name='Traveler_Rig'):
    arm = bpy.data.armatures.new(name)
    ob = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(ob)
    ob.show_in_front = True
    ensure_mode(ob, 'EDIT')
    eb = arm.edit_bones
    for (n, h, t, par, deform, zref, conn) in bone_defs():
        b = eb.new(n); b.head = h; b.tail = t
        b.align_roll(Vector(zref))
        b.use_deform = deform
        if par:
            b.parent = eb[par]; b.use_connect = conn
    # ---- ovládací vrstva
    for side, sfx in ((1, '.L'), (-1, '.R')):
        an = mirror(P['ankle'], side)
        heel = Vector((an.x, an.y + 0.080, 0.0))
        tip = Vector((an.x, an.y - 0.142, 0.0))
        ball = Vector((an.x, an.y - 0.088, 0.0))
        c = eb.new('CTRL_foot' + sfx); c.head = heel; c.tail = heel + Vector((0, -0.16, 0)); c.align_roll(Vector((0, 0, 1)))
        c.parent = eb['root']; c.use_deform = False
        h = eb.new('MCH_heel' + sfx); h.head = heel; h.tail = heel + Vector((0, -0.05, 0)); h.align_roll(Vector((0, 0, 1)))
        h.parent = c; h.use_deform = False
        tt = eb.new('MCH_toetip' + sfx); tt.head = tip; tt.tail = tip + Vector((0, 0.05, 0)); tt.align_roll(Vector((0, 0, 1)))
        tt.parent = h; tt.use_deform = False
        # MCH_footrot nese orientaci chodidla (nezávislou na zvednutí paty)
        fr = eb.new('MCH_footrot' + sfx); fr.head = eb['foot' + sfx].head; fr.tail = eb['foot' + sfx].tail; fr.roll = eb['foot' + sfx].roll
        fr.parent = tt; fr.use_deform = False
        bl = eb.new('MCH_ball' + sfx); bl.head = ball; bl.tail = ball + Vector((0, 0.05, 0)); bl.align_roll(Vector((0, 0, 1)))
        bl.parent = tt; bl.use_deform = False
        ik = eb.new('MCH_ankle' + sfx); ik.head = an; ik.tail = an + Vector((0, 0, 0.05)); ik.align_roll(Vector((0, -1, 0)))
        ik.parent = bl; ik.use_deform = False
        # foot DEF orientace = MCH_footlift (dítě ball – zvedá se s patou)
        fl = eb.new('MCH_footlift' + sfx); fl.head = eb['foot' + sfx].head; fl.tail = eb['foot' + sfx].tail; fl.roll = eb['foot' + sfx].roll
        fl.parent = bl; fl.use_deform = False
        kn = mirror(P['knee'], side)
        pk = eb.new('CTRL_knee' + sfx); pk.head = kn + Vector((0, -0.55, 0)); pk.tail = pk.head + Vector((0, -0.06, 0))
        pk.parent = c; pk.use_deform = False
        el = mirror(P['elbow'], side)
        pe = eb.new('CTRL_elbow' + sfx); pe.head = el + Vector((0.25 * side, 0.45, 0)); pe.tail = pe.head + Vector((0, 0.06, 0))
        pe.parent = eb['root']; pe.use_deform = False
    # ---- nástroj: rám nástroje = rám uchopení pravou rukou v rest póze
    hR = eb['hand.R']
    fR, uR, sR = hand_frame(-1)
    Mh = hR.matrix.copy()  # osy: X, Y=f, Z=s
    grip_world = Mh @ GRIP_LOCAL
    Yt = uR.normalized(); Xt = sR.normalized(); Zt = Xt.cross(Yt)
    for nm, par, deform in (('CTRL_tool', 'root', False), ('prop.R', 'hand.R', True)):
        t = eb.new(nm); t.head = grip_world; t.tail = grip_world + Yt * 0.20
        t.align_roll(Zt); t.parent = eb[par]; t.use_deform = deform
    # gripy jako děti CTRL_tool (mají přesně orientaci ruky)
    gR = eb.new('MCH_grip.R'); gR.head = hR.head; gR.tail = hR.tail; gR.roll = hR.roll; gR.parent = eb['CTRL_tool']; gR.use_deform = False
    # levý grip: otočení o 180° kolem osy Y ruky (viz poznámky), posun po násadě
    T = eb['CTRL_tool'].matrix.copy()
    R_hand = Mh.to_3x3()
    Xl, Yl, Zl = -R_hand.col[0], R_hand.col[1], -R_hand.col[2]
    Rl = Matrix((Xl, Yl, Zl)).transposed()
    handle_pt = T @ Vector((0, TOOL_GRIP_L_Y, 0))
    wrist_L = handle_pt - Rl @ GRIP_LOCAL
    gL = eb.new('MCH_grip.L'); gL.head = wrist_L; gL.tail = wrist_L + Yl * 0.092
    gL.align_roll(Zl); gL.parent = eb['CTRL_tool']; gL.use_deform = False
    ensure_mode(ob, 'OBJECT')
    return ob


def add_driver_roll(ob, bone, expr, prop_owner, axis_index=0, sign=1.0):
    pb = ob.pose.bones[bone]
    pb.rotation_mode = 'XYZ'
    fc = pb.driver_add('rotation_euler', axis_index)
    d = fc.driver; d.type = 'SCRIPTED'
    v = d.variables.new(); v.name = 'roll'; v.type = 'SINGLE_PROP'
    v.targets[0].id = ob
    v.targets[0].data_path = f'pose.bones["{prop_owner}"]["roll"]'
    d.expression = expr.replace('S', f'{sign:.1f}')
    return fc


def setup_constraints(ob):
    ensure_mode(ob, 'POSE')
    pbs = ob.pose.bones
    for pb in pbs:
        pb.rotation_mode = 'QUATERNION'
    for sfx in ('.L', '.R'):
        c = pbs['CTRL_foot' + sfx]
        c['roll'] = 0.0
        c.id_properties_ui('roll').update(min=-1.0, max=2.0, description='Foot roll: <0 pata, 0..1 špička přes bříško, >1 špička')
        # rotace X u kosti mířící -Y se Z nahoru: kladné X = otočení kolem world -X (špička nahoru?)
        # MCH_heel míří -Y (lokální X = world -X), ball/toetip míří +Y (lokální X = world +X)
        add_driver_roll(ob, 'MCH_heel' + sfx, 'S*min(roll,0.0)*0.5', 'CTRL_foot' + sfx, sign=-1.0)
        add_driver_roll(ob, 'MCH_ball' + sfx, 'S*min(max(roll,0.0),1.0)*0.75', 'CTRL_foot' + sfx, sign=1.0)
        add_driver_roll(ob, 'MCH_toetip' + sfx, 'S*max(roll-1.0,0.0)*0.6', 'CTRL_foot' + sfx, sign=1.0)
        ik = pbs['shin' + sfx].constraints.new('IK')
        ik.name = 'IK'; ik.target = ob; ik.subtarget = 'MCH_ankle' + sfx
        ik.pole_target = ob; ik.pole_subtarget = 'CTRL_knee' + sfx
        ik.chain_count = 2; ik.use_stretch = False
        cr = pbs['foot' + sfx].constraints.new('COPY_ROTATION')
        cr.name = 'FootRot'; cr.target = ob; cr.subtarget = 'MCH_footlift' + sfx
        ct = pbs['toe' + sfx].constraints.new('COPY_ROTATION')
        ct.name = 'ToeRot'; ct.target = ob; ct.subtarget = 'MCH_footrot' + sfx
        # ruce
        ika = pbs['forearm' + sfx].constraints.new('IK')
        ika.name = 'IK'; ika.target = ob; ika.subtarget = 'MCH_grip' + sfx
        ika.pole_target = ob; ika.pole_subtarget = 'CTRL_elbow' + sfx
        ika.chain_count = 2; ika.use_stretch = False; ika.influence = 0.0
        ch = pbs['hand' + sfx].constraints.new('COPY_ROTATION')
        ch.name = 'GripRot'; ch.target = ob; ch.subtarget = 'MCH_grip' + sfx; ch.influence = 0.0
    # v002: socket nástroje kopíruje rám nástroje (ruka se může po násadě otáčet)
    ct = pbs['prop.R'].constraints.new('COPY_TRANSFORMS')
    ct.name = 'ToolFrame'; ct.target = ob; ct.subtarget = 'CTRL_tool'
    ensure_mode(ob, 'OBJECT')


def calibrate(ob):
    """Najdi znaménka driverů a pole_angle tak, aby rest póza zůstala rest pózou."""
    pbs = ob.pose.bones
    dg = bpy.context.evaluated_depsgraph_get

    def upd():
        ob.update_tag(refresh={'OBJECT', 'DATA'})
        bpy.context.scene.frame_set(bpy.context.scene.frame_current)

    # ověření foot roll: roll>0 zvedá patu (kotník nahoru), roll<0 zvedá špičku (kotník mírně nahoru, prsty výš)
    for sfx in ('.L', '.R'):
        c = pbs['CTRL_foot' + sfx]
        upd(); z0 = pbs['MCH_ankle' + sfx].head.z; t0 = pbs['MCH_footrot' + sfx].tail.z
        c['roll'] = 0.6; upd(); z1 = pbs['MCH_ankle' + sfx].head.z
        c['roll'] = -0.6; upd(); t1 = pbs['MCH_toetip' + sfx].head.z; tt = pbs['toe' + sfx].tail.z
        c['roll'] = 1.6; upd(); z2 = pbs['MCH_ankle' + sfx].head.z
        c['roll'] = 0.0; upd()
        print('roll check', sfx, round(z1 - z0, 3), round(t1, 3), round(z2 - z0, 3))
        assert z1 > z0 + 0.02 and z2 > z1 and t1 > 0.03, 'foot roll sign'
    # pole angle (pro ruce dočasný cíl v rest poloze zápěstí)
    ensure_mode(ob, 'EDIT')
    for sfx in ('.L', '.R'):
        t = ob.data.edit_bones.new('TMP_wrist' + sfx); h = ob.data.edit_bones['hand' + sfx]
        t.head = h.head; t.tail = h.tail; t.roll = h.roll; t.parent = ob.data.edit_bones['root']
    ensure_mode(ob, 'OBJECT')
    pbs = ob.pose.bones
    for sfx in ('.L', '.R'):
        pbs['forearm' + sfx].constraints['IK'].subtarget = 'TMP_wrist' + sfx
    # v002: pole_angle paží kalibrovat v OHNUTÉ paži (u natažené je úhel neurčitý)
    for sfx, sg in (('.L', 1.0), ('.R', -1.0)):
        con = pbs['forearm' + sfx].constraints['IK']; con.influence = 1.0
        Sh = pbs['upper_arm' + sfx].bone.head_local.copy()
        tw = pbs['TMP_wrist' + sfx]; ce = pbs['CTRL_elbow' + sfx]
        tests = [(Vector((0.05 * sg, -0.22, -0.18)), Vector((0.35 * sg, 0.15, -0.25))),
                 (Vector((-0.10 * sg, -0.25, 0.0)), Vector((0.20 * sg, 0.0, -0.40))),
                 (Vector((0.20 * sg, -0.10, -0.20)), Vector((0.10 * sg, 0.30, -0.10)))]
        best = (1e9, 0)
        for deg in range(-180, 180, 2):
            con.pole_angle = math.radians(deg); err = 0.0
            for wo, po in tests:
                W = Sh + wo
                tw.location = tw.bone.matrix_local.to_3x3().inverted() @ (W - tw.bone.head_local)
                P = (Sh + W) * 0.5 + po
                ce.location = ce.bone.matrix_local.to_3x3().inverted() @ (P - ce.bone.head_local)
                upd()
                E = pbs['forearm' + sfx].head.copy()
                nrm = (W - Sh).cross(P - Sh).normalized()
                side_ok = (E - (Sh + W) * 0.5).dot(P - (Sh + W) * 0.5) > 0
                err += abs((E - Sh).dot(nrm)) + (0 if side_ok else 1.0)
            if err < best[0]:
                best = (err, deg)
        con.pole_angle = math.radians(best[1]); con.influence = 0.0
        tw.location = (0, 0, 0); ce.location = (0, 0, 0); upd()
        print('arm pole calib', sfx, best)
    for chain, sfx_list in (('shin', ('.L', '.R')),):
        for sfx in sfx_list:
            con = pbs[chain + sfx].constraints['IK']
            old = con.influence
            con.influence = 1.0
            par = pbs[chain + sfx].parent
            rest = (ob.matrix_world @ par.bone.tail_local)
            best = (1e9, 0)
            for deg in range(-180, 180, 1):
                con.pole_angle = math.radians(deg); upd()
                e = (ob.matrix_world @ par.tail - rest).length
                if e < best[0]:
                    best = (e, deg)
            con.pole_angle = math.radians(best[1]); con.influence = old; upd()
            print('pole', chain + sfx, best)
    for sfx in ('.L', '.R'):
        pbs['forearm' + sfx].constraints['IK'].subtarget = 'MCH_grip' + sfx
    ensure_mode(ob, 'EDIT')
    for sfx in ('.L', '.R'):
        ob.data.edit_bones.remove(ob.data.edit_bones['TMP_wrist' + sfx])
    ensure_mode(ob, 'OBJECT')


def skin(body, ob):
    """Automatické vážení (bone heat) jen pro deformační kosti kromě prop.R, pak čištění."""
    prop = ob.data.bones['prop.R']; prop.use_deform = False
    for o in bpy.context.selected_objects:
        o.select_set(False)
    body.select_set(True); ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    prop.use_deform = True
    body.vertex_groups.new(name='prop.R')  # prázdná skupina – kost je jen socket


def smooth_weights(body, factor=0.5, repeat=6, keep_rigid=('head',)):
    """Vyhlazení vah (širší přechody v kloubech), normalizace a limit 4 kostí/vrchol (Unity)."""
    import numpy as np
    rigid = {}
    for g in keep_rigid:
        vg = body.vertex_groups.get(g)
        if vg:
            rigid[g] = {v.index: vg.weight(v.index) for v in body.data.vertices
                        if any(gg.group == vg.index for gg in v.groups) and vg.weight(v.index) > 0.98}
    bpy.context.view_layer.objects.active = body
    for o in bpy.context.selected_objects:
        o.select_set(False)
    body.select_set(True)
    bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
    bpy.ops.object.vertex_group_smooth(group_select_mode='BONE_DEFORM', factor=factor, repeat=repeat, expand=0.0)
    bpy.ops.object.vertex_group_limit_total(group_select_mode='BONE_DEFORM', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='BONE_DEFORM', lock_active=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    # tuhé oblasti zpět na 1.0 (hlava se nesmí deformovat)
    for g, verts in rigid.items():
        vg = body.vertex_groups[g]
        for i in verts:
            for gg in list(body.data.vertices[i].groups):
                if gg.group != vg.index:
                    body.vertex_groups[gg.group].remove([i])
            vg.add([i], 1.0, 'REPLACE')


def joint_blend(body, arm_ob, a, b, halfwidth, center_shift=0.0):
    """Přerozdělí váhy mezi kostmi a (rodič) a b (dítě) hladkým přechodem přes kloub.
    Součet wa+wb na vrcholu se zachová, mění se jen jeho rozdělení (smoothstep přes ±halfwidth)."""
    ba, bb = arm_ob.data.bones[a], arm_ob.data.bones[b]
    J = bb.head_local
    da = (ba.tail_local - ba.head_local).normalized(); db = (bb.tail_local - bb.head_local).normalized()
    ax = (da + db).normalized()
    ga, gb = body.vertex_groups[a], body.vertex_groups[b]
    for v in body.data.vertices:
        wa = wb = 0.0
        for g in v.groups:
            if g.group == ga.index: wa = g.weight
            elif g.group == gb.index: wb = g.weight
        tot = wa + wb
        if tot < 1e-4:
            continue
        t = ((v.co - J).dot(ax) - center_shift) / halfwidth
        x = min(max((t + 1) * 0.5, 0.0), 1.0)
        fb = x * x * (3 - 2 * x)
        ga.add([v.index], tot * (1 - fb), 'REPLACE')
        gb.add([v.index], tot * fb, 'REPLACE')


def add_forearm_twist(body, ob, share=0.5):
    import numpy as np
    ensure_mode(ob, 'EDIT')
    eb = ob.data.edit_bones
    for s in ('.L', '.R'):
        fa = eb['forearm' + s]
        t = eb.new('forearm_twist' + s)
        t.head = fa.head.lerp(fa.tail, 0.5); t.tail = fa.tail.copy(); t.roll = fa.roll
        t.parent = fa; t.use_connect = False; t.use_deform = True
    ensure_mode(ob, 'POSE')
    for s in ('.L', '.R'):
        pb = ob.pose.bones['forearm_twist' + s]; pb.rotation_mode = 'QUATERNION'
        c = pb.constraints.new('TRANSFORM'); c.name = 'TwistShare'
        c.target = ob; c.subtarget = 'hand' + s
        c.target_space = 'LOCAL'; c.owner_space = 'LOCAL'
        c.map_from = 'ROTATION'; c.from_rotation_mode = 'SWING_TWIST_Y'
        c.from_min_y_rot = -math.pi; c.from_max_y_rot = math.pi
        c.map_to = 'ROTATION'; c.map_to_x_from = 'X'; c.map_to_y_from = 'Y'; c.map_to_z_from = 'Z'
        c.to_min_y_rot = -math.pi * share; c.to_max_y_rot = math.pi * share
        c.to_min_x_rot = c.to_max_x_rot = 0.0; c.to_min_z_rot = c.to_max_z_rot = 0.0
        c.from_min_x_rot = -math.pi; c.from_max_x_rot = math.pi; c.from_min_z_rot = -math.pi; c.from_max_z_rot = math.pi
        c.mix_mode_rot = 'REPLACE'
    ensure_mode(ob, 'OBJECT')
    # váhy: distální část předloktí plynule na twist kost
    for s in ('.L', '.R'):
        b = ob.data.bones['forearm' + s]
        gf = body.vertex_groups['forearm' + s]
        gt = body.vertex_groups.get('forearm_twist' + s) or body.vertex_groups.new(name='forearm_twist' + s)
        H, T = b.head_local, b.tail_local; d = T - H
        for v in body.data.vertices:
            w = next((g.weight for g in v.groups if g.group == gf.index), 0.0)
            if w < 1e-4:
                continue
            t = (v.co - H).dot(d) / d.length_squared
            x = min(max((t - 0.30) / 0.55, 0.0), 1.0); k = x * x * (3 - 2 * x)
            if k <= 0:
                continue
            gf.add([v.index], w * (1 - k), 'REPLACE'); gt.add([v.index], w * k, 'REPLACE')
    for o in bpy.context.selected_objects:
        o.select_set(False)
    body.select_set(True); bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
    bpy.ops.object.vertex_group_limit_total(group_select_mode='BONE_DEFORM', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='BONE_DEFORM', lock_active=False)
    bpy.ops.object.mode_set(mode='OBJECT')
