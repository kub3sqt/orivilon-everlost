import sys, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import render_util as RU
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=sys.argv[-2])
ob=[o for o in bpy.data.objects if o.type=='MESH'][0]
w=ob.copy(); w.data=ob.data.copy(); bpy.context.scene.collection.objects.link(w)
m=w.modifiers.new('W','WIREFRAME'); m.thickness=0.0016; m.use_replace=True
mat=bpy.data.materials.new('wire'); mat.diffuse_color=(0.05,0.05,0.08,1); w.data.materials.clear(); w.data.materials.append(mat)
mb=bpy.data.materials.new('body'); mb.diffuse_color=(0.85,0.83,0.78,1); ob.data.materials.clear(); ob.data.materials.append(mb)
RU.turn_sheet([ob], sys.argv[-1], views=('front','side_L','back','q34'), res=(500,640))
# detail hand/shoulder
RU.setup_workbench(res=(600,600))
RU.camera(target=Vector((0.33,0,0.85)), d=Vector((0.3,-1,0.2)).normalized(), ortho_scale=0.55); RU.render('/tmp/_d1.png')
RU.camera(target=Vector((0.12,0,0.45)), d=Vector((0.5,-1,0)).normalized(), ortho_scale=0.6); RU.render('/tmp/_d2.png')
RU.camera(target=Vector((0.0,0,0.62)), d=Vector((0,0,-1)).normalized(), ortho_scale=0.6); RU.render('/tmp/_d3.png')
RU.compose(['/tmp/_d1.png','/tmp/_d2.png','/tmp/_d3.png'], sys.argv[-1].replace('.png','_detail.png'))
