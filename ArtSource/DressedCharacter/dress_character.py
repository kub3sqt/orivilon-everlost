import bpy,math,json,array,os
from mathutils import Vector
from math import sin,cos,pi
OUT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath='/Users/kub3sqt/Documents/Codex/2026-10-19/ud/outputs/postavicka_animovana.blend',load_ui=False)
bpy.ops.object.select_all(action='DESELECT')
rig=bpy.data.objects['Character_Rig'];base=bpy.data.objects['Reference_Character'];rig.data.pose_position='REST'
original=[tuple(v.co) for v in base.data.vertices];original_actions=[a.name for a in bpy.data.actions]
clothes=[]
def mat(n,h,rough=.8):
 m=bpy.data.materials.new(n);m.diffuse_color=(*h,1);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*h,1);p.inputs['Roughness'].default_value=rough
 noise=m.node_tree.nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=85;noise.inputs['Detail'].default_value=2
 bump=m.node_tree.nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.16;bump.inputs['Distance'].default_value=.018;m.node_tree.links.new(noise.outputs['Fac'],bump.inputs['Height']);m.node_tree.links.new(bump.outputs['Normal'],p.inputs['Normal']);return m
linen=mat('Natural sand linen',(.48,.32,.17));edge=mat('Linen seams',(.67,.48,.27));leather=mat('Worn umber leather',(.19,.09,.038));hat=mat('Brown felt',(.28,.14,.065));dark=mat('Charcoal shoulder cloth',(.115,.10,.078));pants=mat('Dark earth trousers',(.13,.105,.075));rust=mat('Rust red scarf',(.42,.075,.022));rustlight=mat('Scarf edges',(.55,.13,.042));gold=mat('Antique brass',(.52,.29,.08),.35);sole=mat('Boot soles',(.075,.045,.025));patch=mat('Ochre repair patches',(.29,.17,.075))
def bind(o,bone=None):
 o.parent=rig
 if bone:
  g=o.vertex_groups.new(name=bone);g.add(list(range(len(o.data.vertices))),1,'REPLACE')
 mod=o.modifiers.new('Follow original character rig','ARMATURE');mod.object=rig;clothes.append(o);return o
def mesh(n,vs,fs,m,bone='chest',smooth=True):
 me=bpy.data.meshes.new(n);me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new(n,me);bpy.context.collection.objects.link(o);me.materials.append(m)
 for p in me.polygons:p.use_smooth=smooth
 if bone:bind(o,bone)
 return o
def loft(n,rings,m,bone='chest',N=80,rag=0):
 vs=[]
 for j,(x,y,z,rx,ry) in enumerate(rings):
  for i in range(N):
   a=i*2*pi/N;dz=rag*(.55*sin(a*13)+.35*cos(a*19)) if j==0 else 0
   vs.append((x+rx*cos(a),y+ry*sin(a),z+dz))
 fs=[(j*N+i,j*N+(i+1)%N,(j+1)*N+(i+1)%N,(j+1)*N+i) for j in range(len(rings)-1) for i in range(N)]
 o=mesh(n,vs,fs,m,bone);s=o.modifiers.new('Cloth thickness','SOLIDIFY');s.thickness=.025;return o
def tube(n,pts,r,m,bone='chest'):
 cu=bpy.data.curves.new(n,'CURVE');cu.dimensions='3D';cu.bevel_depth=r;cu.bevel_resolution=3;sp=cu.splines.new('BEZIER');sp.bezier_points.add(len(pts)-1)
 for q,p in zip(sp.bezier_points,pts):q.co=p;q.handle_left_type=q.handle_right_type='AUTO'
 o=bpy.data.objects.new(n,cu);bpy.context.collection.objects.link(o);cu.materials.append(m);bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.convert(target='MESH');o.select_set(False);return bind(o,bone)
def box(n,loc,dim,m,bone='hips',bev=.05):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=n;o.dimensions=dim;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);mo=o.modifiers.new('Soft leather edges','BEVEL');mo.width=bev;mo.segments=4;bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mo.name);o.data.materials.append(m)
 for p in o.data.polygons:p.use_smooth=True
 # Bake locations so all clothing uses the rig coordinate system.
 for v in o.data.vertices:v.co+=o.location
 o.location=(0,0,0);o.select_set(False);return bind(o,bone)
def ribbon(n,pts,width,m,bone='chest'):
 vs=[]
 for x,y,z in pts:vs.extend([(x-width/2,y,z),(x+width/2,y,z)])
 o=mesh(n,vs,[(i*2,i*2+1,i*2+3,i*2+2) for i in range(len(pts)-1)],m,bone);s=o.modifiers.new('Leather thickness','SOLIDIFY');s.thickness=.04;b=o.modifiers.new('Rounded edges','BEVEL');b.width=.025;b.segments=3;return o
# Clothes copied from the original surface keep its exact weights and proportions.
gnames={g.index:g.name for g in base.vertex_groups}
def shell(n,predicate,m,offset):
 faces=[p for p in base.data.polygons if predicate(p.center if False else sum((base.data.vertices[i].co for i in p.vertices),Vector())/len(p.vertices),p)]
 ids=sorted({i for p in faces for i in p.vertices});mp={v:i for i,v in enumerate(ids)}
 vs=[base.data.vertices[i].co+base.data.vertices[i].normal*offset for i in ids]
 o=mesh(n,vs,[tuple(mp[i] for i in p.vertices) for p in faces],m,None)
 for g in base.vertex_groups:o.vertex_groups.new(name=g.name)
 for i,src in enumerate(ids):
  for g in base.data.vertices[src].groups:o.vertex_groups[g.group].add([i],g.weight,'REPLACE')
 bind(o);return o
shell('Fitted trousers',lambda c,p:c.z<2.3 and c.z> .78 and abs(c.x)<1.02,pants,.045)
shell('Leather boots',lambda c,p:c.z<1.02,leather,.035)
shell('Dark boot sole',lambda c,p:c.z<.285,sole,.045)
shell('Linen shirt and short sleeves',lambda c,p:2.15<c.z<3.78 and abs(c.x)<(.98 if c.z>3.12 else .77),linen,.055)
# A separate hanging hem gives the tunic a cloth silhouette instead of body paint.
loft('Torn tunic hem',[(0,0,1.91,.90,.54),(0,0,2.16,.84,.53),(0,0,2.40,.70,.47),(0,0,2.65,.65,.46)],linen,'hips',rag=.085)
shell('Ragged shoulder mantle',lambda c,p:3.25+.055*sin(c.x*36)<c.z<3.84 and abs(c.x)<1.00,dark,.13)
# Wrinkled scarf wraps and hanging ends.
for k in range(4):
 pts=[]
 for i in range(65):
  a=2*pi*i/64;pts.append((.39*cos(a),.34*sin(a),3.85+k*.085+.035*sin(2*a+k)))
 tube('Scarf folded wrap %d'%k,pts,.103,rust if k%2 else rustlight,'neck')
ribbon('Scarf front tail',[(.23,-.37,3.91),(.33,-.55,3.62),(.28,-.60,3.34),(.24,-.60,3.04)],.24,rust,'chest')
ribbon('Scarf back tail',[(.0,.37,3.91),(-.27,.48,3.52),(-.36,.56,3.13),(-.43,.58,2.91)],.20,rust,'chest')
# Broad asymmetric felt hat, smooth crown and gently bent rim.
loft('Soft weathered hat crown',[(0,0,5.45,1.04,.99),(-.04,.02,5.63,1.0,.94),(-.10,.03,6.04,.79,.76),(-.20,.04,6.34,.53,.49),(-.29,.03,6.43,.002,.002)],hat,'head')
N=128;vs=[]
for j in range(5):
 t=j/4
 for i in range(N):
  a=2*pi*i/N;r=1.00+.59*t;vs.append((r*cos(a),(r*.88)*sin(a),5.48-.20*t+.10*t*sin(3*a)+.06*t*cos(5*a)))
o=mesh('Bent broad hat brim',vs,[(j*N+i,j*N+(i+1)%N,(j+1)*N+(i+1)%N,(j+1)*N+i) for j in range(4) for i in range(N)],hat,'head');s=o.modifiers.new('Felt thickness','SOLIDIFY');s.thickness=.055
loft('Hat charcoal band',[(0,0,5.49,1.053,1.0),(-.025,.01,5.69,.998,.945)],dark,'head')
# Belt, strap, pouch, brass buckles.
loft('Waist leather belt',[(0,0,2.29,.81,.555),(0,0,2.47,.76,.535)],leather,'hips')
for n,x,z,w,h in [('Belt buckle',0,2.38,.25,.22),('Strap buckle',.15,2.97,.18,.19)]:
 y=-.585 if n.startswith('Belt') else -.64
 tube(n,[(x-w/2,y,z-h/2),(x+w/2,y,z-h/2),(x+w/2,y,z+h/2),(x-w/2,y,z+h/2),(x-w/2,y,z-h/2)],.025,gold,'hips' if n.startswith('Belt') else 'chest')
ribbon('Diagonal leather baldric',[(-.53,-.35,3.69),(-.40,-.54,3.48),(-.13,-.62,3.18),(.20,-.61,2.90),(.60,-.48,2.47)],.145,leather)
ribbon('Baldric back',[(-.53,.31,3.69),(-.32,.51,3.36),(.10,.56,2.95),(.60,.43,2.47)],.145,leather)
box('Hip satchel',(-.65,-.53,2.12),(.49,.27,.55),leather)
box('Satchel flap',(-.65,-.69,2.27),(.53,.065,.28),hat)
box('Satchel fastening',(-.65,-.745,2.20),(.07,.04,.14),gold,bev=.018)
for s,side in [(1,'L'),(-1,'R')]:
 # Wrist wraps follow forearms, no change to original mitten hands.
 b=rig.data.bones['forearm.'+side];a=b.head_local.lerp(b.tail_local,.68);d=(b.tail_local-b.head_local).normalized();u=d.cross(Vector((0,1,0))).normalized();v=d.cross(u)
 vs=[]
 for t in [-.13,.13]:
  for i in range(64):vs.append(a+d*t+(u*cos(i*2*pi/64)+v*sin(i*2*pi/64))*.285)
 mesh('Leather wrist wrap '+side,vs,[(i,(i+1)%64,64+(i+1)%64,64+i) for i in range(64)],leather,'forearm.'+side)
 loft('Boot folded cuff '+side,[(s*.65,0,.88,.285,.32),(s*.64,0,1.05,.29,.33)],hat,'shin.'+side)
 box('Trouser repair '+side,(s*.58,-.366,1.55),(.26,.045,.32),patch,'thigh.'+side,.025)
 for j in range(4):
  z=.49+j*.09;tube('Boot cross lace '+side+str(j),[(s*.52,-.67+j*.075,z),(s*.80,-.67+j*.075,z+.065)],.017,edge,'foot.'+side)
 for j in range(4):tube('Patch stitch '+side+str(j),[(s*.48,-.396,1.43+j*.075),(s*.53,-.40,1.46+j*.075)],.009,edge,'thigh.'+side)
# Small repaired seam on felt.
for i in range(5):tube('Hat repair stitch '+str(i),[(-.65+i*.06,-.83,5.79+i*.025),(-.62+i*.06,-.85,5.90+i*.025)],.012,edge,'head')
assert original==[tuple(v.co) for v in base.data.vertices]
assert original_actions==[a.name for a in bpy.data.actions]
rig.data.pose_position='POSE'
tracks=rig.animation_data.nla_tracks
for t in tracks:t.mute=t.name!='Idle'
bpy.context.scene.frame_set(1)
# Save original studio, rig, actions and added clothing together.
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/DressedWanderer.blend')
# Export just the rigged character. All original NLA clips remain separate.
bpy.ops.object.select_all(action='DESELECT')
for o in [base,rig]+clothes:o.select_set(True)
bpy.context.view_layer.objects.active=rig
for t in tracks:t.mute=False
bpy.ops.export_scene.gltf(filepath=OUT+'/DressedWanderer.glb',use_selection=True,export_format='GLB',export_animations=True,export_animation_mode='NLA_TRACKS',export_skins=True,export_yup=True)
for t in tracks:t.mute=t.name!='Idle'
# Lightweight real 3D browser geometry, at the original animation pose.
bpy.context.scene.frame_set(1);dg=bpy.context.evaluated_depsgraph_get();buf=array.array('f')
for src in [base]+clothes:
 ev=src.evaluated_get(dg);me=ev.to_mesh();me.calc_loop_triangles();mw=ev.matrix_world;nm=mw.to_3x3().inverted().transposed()
 for tri in me.loop_triangles:
  material=me.materials[tri.material_index] if me.materials else linen;c=material.diffuse_color[:3]
  for vi in tri.vertices:
   p=mw@me.vertices[vi].co;n=(nm@me.vertices[vi].normal).normalized();buf.extend((*p,*n,*[max(0,x)**(1/2.2) for x in c]))
 ev.to_mesh_clear()
with open(OUT+'/model.bin','wb') as f:buf.tofile(f)
json.dump({'file':'model.bin','vertices':len(buf)//9},open(OUT+'/model.json','w'))
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=24;s.render.resolution_x=850;s.render.resolution_y=1000;s.render.resolution_percentage=100;s.view_settings.exposure=-.7;s.render.filepath=OUT+'/inspection.png';bpy.ops.render.render(write_still=True)
json.dump({'original_vertices_unchanged':True,'actions_preserved':original_actions,'clothing_objects':len(clothes),'original_vertex_count':len(original)},open(OUT+'/validation.json','w'),indent=2)
