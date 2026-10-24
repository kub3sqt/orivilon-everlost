"""Pomocné funkce pro pózování deformační kostry v sémantických (světových) pojmech."""
import math, bpy
from mathutils import Vector, Matrix, Quaternion, Euler


def R_rest(pb):
    return pb.bone.matrix_local.to_3x3()


def rot_world(pb, W):
    """Otoč kost o rotaci W vyjádřenou v armature prostoru (vůči rest orientaci rodiče)."""
    R = R_rest(pb)
    q = (R.inverted() @ W.to_3x3() @ R).to_quaternion()
    pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = q


def eul(x=0.0, y=0.0, z=0.0, order='XYZ'):
    """Euler ve stupních -> Matrix. X = předklon(+)/záklon, Y = úklon, Z = rotace kolem svislice."""
    return Euler((math.radians(x), math.radians(y), math.radians(z)), order).to_matrix()


def aim(pb, direction, zref):
    """Orientuj kost (Y=direction, Z~zref) vůči rest rámci rodiče."""
    y = Vector(direction).normalized()
    z = Vector(zref); z = (z - y * z.dot(y)).normalized()
    x = y.cross(z)
    O = Matrix((x, y, z)).transposed()
    q = (R_rest(pb).inverted() @ O).to_quaternion()
    pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = q


def local_x(pb, deg):
    pb.rotation_mode = 'QUATERNION'
    pb.rotation_quaternion = Quaternion((1, 0, 0), math.radians(deg))


def clear(ob):
    for pb in ob.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
        if 'roll' in pb.keys():
            pb['roll'] = 0.0


def update():
    bpy.context.view_layer.update()
    bpy.context.scene.frame_set(bpy.context.scene.frame_current)


def swing_to(pb, direction, twist_deg=0.0):
    """Natoč kost do směru minimální rotací z rest směru (+ volitelný twist kolem nové osy)."""
    d0 = (pb.bone.tail_local - pb.bone.head_local).normalized()
    d = Vector(direction).normalized()
    W = d0.rotation_difference(d).to_matrix()
    if twist_deg:
        W = Quaternion(d, math.radians(twist_deg)).to_matrix() @ W
    rot_world(pb, W)


def move_world(pb, delta):
    """Posun kosti v armature prostoru (pro kosti bez rodiče nebo s rodičem v rest)."""
    pb.location = R_rest(pb).inverted() @ Vector(delta)
