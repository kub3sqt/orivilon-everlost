"""Idle – jednoduchý neutrální postoj (dech, lehký přenos váhy) jen pro funkční náhled."""
import sys, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import anim_lib as A
from anim_lib import Rig


def build(rig):
    act = A.new_action(rig.ob, 'Idle')
    K = {0: (0.000, -0.004, 0, 0), 20: (0.004, -0.008, 1.5, 1), 40: (0.001, -0.003, -0.5, 0), 60: (0.000, -0.004, 0, 0)}
    for f, (hx, hz, ch, sw) in K.items():
        rig.reset()
        rig.hips((hx, 0, hz), (1, -sw * 0.8, 0))
        rig.fk('spine', (1, 0, 0)); rig.fk('chest', (-ch, sw * 0.4, 0)); rig.fk('neck', (ch * 0.5, 0, 0)); rig.fk('head', (0, 0, sw))
        rig.arm('.L', swing=2 + ch, abd=12, elbow=14 + ch * 2, fingers=18, thumb=6)
        rig.arm('.R', swing=2 + ch, abd=12, elbow=14 + ch * 2, fingers=18, thumb=6)
        rig.foot('.L', loc=(0.02, 0, 0), yaw=-6); rig.foot('.R', loc=(-0.02, 0, 0), yaw=6)
        rig.key(f)
    A.finalize(act, 0, 60, cyclic=True)
    act['in_place'] = True; act['speed_mps'] = 0.0
    return act


if __name__ == '__main__':
    src, dst = sys.argv[-2], sys.argv[-1]
    bpy.ops.wm.open_mainfile(filepath=src)
    build(Rig(bpy.data.objects['Traveler_Rig']))
    bpy.ops.wm.save_as_mainfile(filepath=dst)
