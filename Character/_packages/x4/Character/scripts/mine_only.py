"""Rychlá iterace: přestaví jen akci Mine. python3 mine_only.py -- in.blend out.blend"""
import sys, os, bpy
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import anim_tools as AT
from anim_lib import Rig
src, dst = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.open_mainfile(filepath=src)
AT.build_mine(Rig(bpy.data.objects['Traveler_Rig']))
bpy.ops.wm.save_as_mainfile(filepath=dst)
