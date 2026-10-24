import sys, bpy, numpy as np, colorsys
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import render_util as RU
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=sys.argv[-3])
body=bpy.data.objects['Body']; me=body.data
groups=sys.argv[-2].split(',')
ca=me.color_attributes.new('W','FLOAT_COLOR','POINT')
cols=[(1,0.2,0.2),(0.2,0.9,0.2),(0.2,0.4,1),(1,0.9,0.1),(0.9,0.2,0.9),(0.1,0.9,0.9)]
for v in me.vertices:
    c=np.array([0.15,0.15,0.15])
    for gg in v.groups:
        n=body.vertex_groups[gg.group].name
        if n in groups: c=c+np.array(cols[groups.index(n)])*gg.weight
    ca.data[v.index].color=(*np.clip(c,0,1),1)
me.color_attributes.active_color=ca
RU.setup_workbench(res=(500,500)); s=bpy.context.scene; s.display.shading.color_type='VERTEX'; s.display.shading.show_cavity=False
bpy.data.objects['Traveler_Rig'].hide_render=True
out=[]
for i,(t,d,sc) in enumerate([((0.3,0,0.85),(0,-1,0),0.6),((0.3,0,0.85),(0,1,0.3),0.6),((0.1,0,0.4),(0,-1,0),0.7),((0.1,0,0.4),(1,0,0),0.7)]):
    RU.camera(target=Vector(t),d=Vector(d).normalized(),ortho_scale=sc); p=f'/tmp/_wv{i}.png'; RU.render(p); out.append(p)
RU.compose(out, sys.argv[-1], cols=4)
