import sys, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import render_util as RU
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=sys.argv[-2])
ob=bpy.data.objects['Body']; ob.data.materials.clear(); ob.data.materials.append(RU.ivory_material())
RU.add_floor()
RU.setup_cycles(res=(420,560))
out=[]
for v in ['front','q34','side_L','back']:
    RU.camera(target=Vector((0,0,0.76)), d=RU.VIEWS[v]+Vector((0,0,0.12)), ortho=False, lens=85, dist=5.2)
    p=f'/tmp/_b_{v}.png'; RU.render(p); out.append(p)
RU.compose(out, sys.argv[-1])
