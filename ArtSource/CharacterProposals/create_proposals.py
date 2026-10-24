"""Three editable 3D player-character proposals. No renders or game assets are replaced."""
from pathlib import Path
import bpy, math, random, os, json, struct
from mathutils import Vector
SOURCE=Path(__file__).resolve().parents[1]/'Traveler/create_traveler.py'
# Reuse the already verified mesh authoring primitives, not the rejected character geometry.
exec(compile(SOURCE.read_text().split('# The pelvis sits lower:')[0],str(SOURCE),'exec'),globals())
OUT=Path(__file__).resolve().parent
(OUT/'textures').mkdir(exist_ok=True)
random.seed(124)

def material_set(key,colours):
 result={}
 for name,colour in colours.items():
  c=tuple(v**2.2 for v in colour)
  m=bpy.data.materials.new(key+'_'+name); m.diffuse_color=tuple(colour)+(1,); m.use_nodes=True
  nodes=m.node_tree.nodes; links=m.node_tree.links; bs=nodes.get('Principled BSDF')
  bs.inputs['Base Color'].default_value=c+(1,); bs.inputs['Roughness'].default_value=.83
  if name=='Metal': bs.inputs['Metallic'].default_value=.65; bs.inputs['Roughness'].default_value=.4
  elif name=='Glow':
   bs.inputs['Emission Color'].default_value=(1,.24,.025,1); bs.inputs['Emission Strength'].default_value=.8
  elif name!='Skin':
   pos=nodes.new('ShaderNodeNewGeometry'); noise=nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=19
   noise.inputs['Detail'].default_value=3; noise.inputs['Roughness'].default_value=.68; links.new(pos.outputs['Position'],noise.inputs['Vector'])
   ramp=nodes.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].position=.15; ramp.color_ramp.elements[0].color=(.64,.58,.52,1)
   ramp.color_ramp.elements[1].position=.83; ramp.color_ramp.elements[1].color=(1.12,1.08,1.02,1); links.new(noise.outputs['Fac'],ramp.inputs[0])
   mix=nodes.new('ShaderNodeMixRGB'); mix.blend_type='MULTIPLY'; mix.inputs[0].default_value=1; mix.inputs[1].default_value=c+(1,)
   links.new(ramp.outputs[0],mix.inputs[2]); links.new(mix.outputs[0],bs.inputs['Base Color'])
   fine=nodes.new('ShaderNodeTexNoise'); fine.inputs['Scale'].default_value=220; fine.inputs['Detail'].default_value=2
   links.new(pos.outputs['Position'],fine.inputs['Vector']); bump=nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.20; bump.inputs['Distance'].default_value=.003
   links.new(fine.outputs['Fac'],bump.inputs['Height']); links.new(bump.outputs['Normal'],bs.inputs['Normal'])
  result[name]=m
 return result

PALETTES=[
 dict(Skin=(.92,.865,.73),Shirt=(.76,.69,.53),Cloth=(.24,.34,.24),Lining=(.36,.43,.29),Scarf=(.56,.24,.15),Pants=(.28,.29,.25),Leather=(.33,.20,.12),Edge=(.44,.30,.18),Thread=(.65,.47,.25),Metal=(.65,.44,.19),Glow=(.95,.47,.12)),
 dict(Skin=(.92,.865,.73),Shirt=(.66,.47,.27),Cloth=(.40,.19,.18),Lining=(.53,.29,.22),Scarf=(.34,.20,.18),Pants=(.29,.245,.205),Leather=(.29,.18,.105),Edge=(.43,.29,.16),Thread=(.63,.43,.235),Metal=(.58,.38,.16),Glow=(.95,.47,.12)),
 dict(Skin=(.92,.865,.73),Shirt=(.77,.725,.60),Cloth=(.24,.31,.42),Lining=(.34,.40,.48),Scarf=(.70,.47,.16),Pants=(.28,.29,.31),Leather=(.31,.21,.14),Edge=(.45,.32,.20),Thread=(.64,.50,.29),Metal=(.65,.40,.20),Glow=(1,.46,.08))]

def line(name,points,radius,mat,p):
 bpy.ops.object.select_all(action='DESELECT'); return tube(name,points,radius,mat,p)

def frame(name,x,y,z,w,h,p):
 for dx in [-w/2,w/2]: box(name+'Side',(x+dx,y,z),(.009,.018,h),'Metal',p,.003)
 for dz in [-h/2,h/2]: box(name+'Rail',(x,y,z+dz),(w,.018,.009),'Metal',p,.003)
 box(name+'Pin',(x,y-.01,z),(.007,.009,h*.7),'Metal',p,.002)

def scarf(torso):
 rings('ScarfWrap',[(1.32,.152,.12,0,-.01),(1.35,.19,.151,0,-.018),(1.385,.18,.145,0,-.015),(1.415,.142,.113,0,-.005)],'Scarf',torso,n=64,fold=.012)
 rings('ScarfFold',[(1.35,.157,.126,0,-.012),(1.375,.193,.154,0,-.016),(1.402,.17,.136,0,-.012)],'Scarf',torso,n=64)
 ribbon('ScarfDrape',[(0,-.165,1.37,.27),(.018,-.193,1.32,.29),(.035,-.20,1.265,.19),(.05,-.198,1.225,.065)],'Scarf',torso)

def body(key):
 root=joint(key,(0,0,0)); hips=joint('Hips',(0,0,.82),root); torso=joint('Torso',(0,0,1.02),hips); head=joint('Head',(0,0,1.47),torso)
 # Slightly tapered oval face, one continuous smooth mesh.
 ball('Face',(0,-.035,1.565),(.207,.182,.239),'Skin',head)
 rings('Shirt',[(.70,.26,.158,0,0),(.755,.27,.169,0,0),(.84,.237,.153,0,0),(.94,.23,.155,0,0),(1.05,.25,.168,0,0),(1.16,.274,.173,0,0),(1.25,.28,.17,0,0),(1.31,.245,.147,0,0),(1.35,.16,.105,0,0)],'Shirt',torso,n=64,torn=True,fold=.02)
 for side,sgn in [('L',-1),('R',1)]:
  x=.19*sgn; leg=joint('Leg'+side,(x,0,.81),hips); knee=joint('Knee'+side,(x,0,.43),leg)
  rings('PantsUpper'+side,[(.39,.128,.126,x,0),(.46,.145,.138,x,0),(.57,.151,.147,x,.004),(.72,.143,.135,x,0),(.83,.12,.12,x,0)],'Pants',leg,n=48,fold=.023)
  rings('PantsLower'+side,[(.24,.109,.108,x,0),(.31,.128,.119,x,0),(.39,.13,.124,x,0),(.46,.133,.127,x,0)],'Pants',knee,n=48,fold=.02)
  # Slim layered soles, rounded welted toes and soft shaft.
  box('Sole'+side,(x,-.052,.028),(.245,.37,.045),'Leather',knee,.024)
  box('BootToe'+side,(x,-.08,.112),(.235,.345,.165),'Leather',knee,.06)
  rings('BootShaft'+side,[(.105,.108,.106,x,.012),(.19,.113,.109,x,.015),(.27,.12,.118,x,.014),(.295,.125,.12,x,.012)],'Leather',knee,n=48,fold=.014)
  rings('BootCuff'+side,[(.267,.129,.125,x,.012),(.28,.135,.131,x,.012),(.31,.129,.126,x,.01)],'Edge',knee,n=48)
  for j in range(3):
   yy=-.178+j*.035
   line('BootLace'+side+str(j),[(x-.049,yy-.008,.184),(x,yy,.197),(x+.049,yy+.008,.184)],.0035,'Thread',knee)
  # Two offset straps and real metal buckles.
  box('BootBuckle'+side,(x+sgn*.119,-.055,.25),(.012,.038,.035),'Metal',knee,.005)
  arm=joint('Arm'+side,(sgn*.282,0,1.295),torso); elbow=joint('Elbow'+side,(sgn*.359,-.012,1.085),arm); hand=joint('Hand'+side,(sgn*.386,-.04,.918),elbow)
  rings('Sleeve'+side,[(1.035,.077,.085,sgn*.369,-.018),(1.06,.101,.103,sgn*.36,-.013),(1.12,.112,.112,sgn*.345,-.008),(1.21,.108,.111,sgn*.31,0),(1.29,.09,.097,sgn*.282,0),(1.315,.065,.075,sgn*.274,0)],'Shirt',arm,n=48,fold=.025)
  rings('RolledCuff'+side,[(1.035,.082,.089,sgn*.369,-.018),(1.047,.098,.101,sgn*.367,-.016),(1.075,.099,.102,sgn*.36,-.014)],'Shirt',elbow,n=48,fold=.016)
  ball('Wrist'+side,(sgn*.381,-.03,.988),(.062,.065,.08),'Skin',elbow)
  rings('Bracer'+side,[(.935,.069,.071,sgn*.386,-.039),(.953,.077,.078,sgn*.384,-.037),(1.008,.075,.075,sgn*.377,-.029)],'Leather',elbow,n=40,fold=.008)
  box('BracerStrap'+side,(sgn*.383,-.111,.974),(.135,.014,.022),'Edge',elbow,.005)
  parts=[ball('Palm'+side,(sgn*.389,-.04,.883),(.07,.061,.075),'Skin',hand),ball('Thumb'+side,(sgn*.337,-.075,.898),(.029,.035,.048),'Skin',hand)]
  for f in range(3): parts.append(ball('Finger'+side+str(f),(sgn*(.358+f*.029),-.046,.843),(.022,.05,.026),'Skin',hand))
  bpy.ops.object.select_all(action='DESELECT')
  for o in parts:o.select_set(True)
  bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join(); handmesh=bpy.context.object
  mod=handmesh.modifiers.new('Smooth hand','REMESH'); mod.mode='VOXEL'; mod.voxel_size=.0055; mod.use_smooth_shade=True; apply(handmesh,mod)
  mod=handmesh.modifiers.new('Soft fingers','SMOOTH'); mod.factor=.4; mod.iterations=3; apply(handmesh,mod)
  for p in handmesh.data.polygons:p.use_smooth=True
 rings('WaistBelt',[(.865,.253,.173,0,0),(.881,.258,.177,0,0),(.925,.251,.17,0,0),(.934,.245,.165,0,0)],'Leather',hips,n=64)
 frame('BeltBuckle',.015,-.184,.901,.08,.065,hips)
 # Front shirt placket and small fabric-covered buttons.
 ribbon('ShirtPlacket',[(0,-.17,1.29,.035),(0,-.178,1.17,.035),(0,-.17,1.04,.035)],'Shirt',torso)
 for z in [1.19,1.12,1.05]:ball('ShirtButton',(0,-.184,z),(.009,.005,.009),'Edge',torso)
 return root,hips,torso,head

def cape(name,p,bottom=.91,spread=.46,asym=.08):
 verts=[]; rows=18; cols=88
 for j in range(rows):
  t=j/(rows-1)
  for i in range(cols):
   a=-math.pi/2+.48+(math.tau-.96)*i/(cols-1)
   radial=(.155+(spread-.155)*t)*(1+.025*math.sin(a*8+t*5)*math.sin(t*math.pi))
   z=1.375+(bottom-1.375)*t+asym*math.cos(a)*t
   z+=.008*math.sin(a*15)*t*t
   verts.append((radial*math.cos(a),radial*.68*math.sin(a)+.015,z))
 faces=[]
 for j in range(rows-1):
  for i in range(cols-1):
   k=j*cols+i;faces.append((k,k+1,k+cols+1,k+cols))
 mesh(name,verts,faces,'Cloth',p,True,.012,.002)
 for index in [0,cols-1]:
  line(name+'FrontSeam'+str(index),[verts[j*cols+index] for j in range(rows)],.0028,'Thread',p)
 line(name+'HemSeam',[verts[(rows-1)*cols+i] for i in range(cols)],.0021,'Lining',p)
 # Individual stitches along the front edges, physically modeled.
 for index in [0,cols-1]:
  for row in range(3,rows-1,2):
   v=Vector(verts[row*cols+index]);delta=Vector((.013 if index==0 else -.013,-.003,.003))
   line('CapeStitch'+str(index)+'_'+str(row),[v,v+delta],.0015,'Thread',p)


def satchel(p,x=-.238,z=.81,small=False):
 width=.19 if small else .23; height=.18 if small else .23
 box('Satchel',(x,-.165,z),(width,.139,height),'Leather',p,.031)
 verts=[(x-width*.52,-.238,z+height*.5),(x+width*.52,-.238,z+height*.5),(x+width*.5,-.254,z+.005),(x+width*.35,-.265,z-.025),(x-width*.35,-.265,z-.025),(x-width*.5,-.254,z+.005)]
 mesh('SatchelFlap',verts,[tuple(range(6))],'Edge',p,True,.012,.009)
 box('SatchelClasp',(x,-.275,z-.005),(.027,.018,.055),'Leather',p,.007)
 ball('SatchelButton',(x,-.286,z-.012),(.01,.004,.01),'Metal',p)
 for i in range(6):
  xx=x-width*.38+i*width*.15
  line('PouchStitch'+str(i),[(xx,-.27,z+.02),(xx+.009,-.27,z+.02)],.0015,'Thread',p)
 strap('SatchelStrap',(-.20,-.18,1.31),(.22,-.187,.88),.052,.019,'Leather',p)


def hood(p):
 verts=[]; rows=24; cols=96
 for j in range(rows):
  t=.995*j/(rows-1); shrink=math.sqrt(1-t*t)
  for i in range(cols):
   a=math.tau*i/cols
   rx=.252*shrink; rz=.31*shrink
   wrinkle=.006*math.sin(a*7+t*13)*math.sin(math.pi*t)
   x=(rx+wrinkle)*math.cos(a)-.012*math.sin(a)*t
   z=1.615+(rz+wrinkle)*math.sin(a)+.018*t
   y=-.193+.46*t+.012*math.cos(5*a)*math.sin(t*math.pi)
   verts.append((x,y,z))
 faces=[]
 for j in range(rows-1):
  for i in range(cols):
   k=j*cols+i;faces.append((k,j*cols+(i+1)%cols,(j+1)*cols+(i+1)%cols,(j+1)*cols+i))
 faces.append(tuple((rows-1)*cols+i for i in range(cols)))
 mesh('TravelerHood',verts,faces,'Cloth',p,True,.013,.002)
 line('RolledHoodEdge',verts[:cols]+[verts[0]],.008,'Lining',p)
 for i in range(2,cols-2,4):
  a=Vector(verts[i]); b=Vector(verts[i+1]);line('HoodStitch'+str(i),[a+Vector((0,-.009,0)),b+Vector((0,-.009,0))],.0017,'Thread',p)
 # Restrained leaf-shaped clasp at the neckline.
 ball('LeafClasp',(.10,-.183,1.327),(.035,.009,.018),'Metal',p)
 line('LeafVein',[(.075,-.195,1.32),(.125,-.195,1.336)],.002,'Edge',p)


def floppy_hat(p):
 n=120;levels=[(.215,.19,1.795),(.30,.265,1.782),(.425,.34,1.745),(.432,.346,1.758),(.30,.265,1.797),(.215,.19,1.81)];v=[]
 for k,(rx,ry,z) in enumerate(levels):
  for i in range(n):
   a=i*math.tau/n;f=1 if k in (2,3) else .25
   ripple=(.027*math.sin(3*a+.2)-.04*max(0,-math.sin(a))+.02*math.cos(2*a))*f
   v.append((rx*math.cos(a),ry*math.sin(a),z+ripple))
 faces=[]
 for k in range(6):
  for i in range(n):faces.append((k*n+i,k*n+(i+1)%n,((k+1)%6)*n+(i+1)%n,((k+1)%6)*n+i))
 mesh('SoftHatBrim',v,faces,'Edge',p)
 rings('CrumpledHat',[(1.795,.216,.19,0,0),(1.86,.21,.185,-.004,.005),(1.92,.184,.163,-.025,.01),(1.98,.139,.126,-.05,.018),(2.015,.085,.077,-.07,.01),(2.025,.06,.055,-.072,.01)],'Edge',p,n=72,fold=.045)
 rings('HatBand',[(1.815,.232,.208,0,0),(1.842,.229,.204,-.002,0),(1.872,.219,.20,-.006,.004)],'Leather',p,n=64)
 for i in range(4):
  z=1.89+i*.016
  line('HatRepair'+str(i),[(.157,-.139,z),(.18,-.133,z+.004)],.002,'Thread',p)


def beret(p):
 rings('SoftCap',[(1.777,.205,.184,0,0),(1.81,.235,.202,-.025,.015),(1.87,.255,.212,-.052,.018),(1.925,.19,.174,-.075,.022),(1.95,.105,.10,-.078,.018),(1.957,.04,.04,-.077,.018)],'Cloth',p,n=80,fold=.045)
 rings('CapBand',[(1.758,.208,.185,0,-.004),(1.782,.21,.188,0,-.003),(1.802,.214,.189,-.008,0)],'Leather',p,n=64)
 # A soft short visor changes the cap silhouette and shades the smooth face.
 verts=[]
 for row in range(5):
  t=row/4
  for i in range(33):
   a=-math.pi*.83+math.pi*.66*i/32
   verts.append(((.19+.04*t)*math.cos(a),(.17+.09*t)*math.sin(a)-.02,1.775-.025*t+.008*math.cos(a*3)))
 faces=[]
 for row in range(4):
  for i in range(32):
   j=row*33+i;faces.append((j,j+1,j+34,j+33))
 mesh('CapVisor',verts,faces,'Cloth',p,True,.008,.003)
 ball('CapButton',(-.09,.01,1.966),(.015,.015,.009),'Edge',p)


def lantern(p):
 x=.28;y=-.14;z=.705
 rings('LanternBase',[(z-.14,.066,.066,x,y),(z-.116,.073,.073,x,y),(z-.10,.061,.061,x,y)],'Metal',p,n=48)
 rings('LanternTop',[(z+.07,.064,.064,x,y),(z+.095,.072,.072,x,y),(z+.13,.041,.041,x,y),(z+.14,.027,.027,x,y)],'Metal',p,n=48)
 ball('AmberLanternGlass',(x,y,z-.013),(.055,.055,.104),'Glow',p)
 for i in range(4):
  a=i*math.tau/4;xx=x+.063*math.cos(a);yy=y+.063*math.sin(a)
  line('LanternRail'+str(i),[(xx,yy,z-.105),(xx,yy,z+.077)],.0045,'Metal',p)
 line('LanternHandle',[(x-.043,y,z+.12),(x-.052,y,z+.19),(x,y,z+.216),(x+.052,y,z+.19),(x+.043,y,z+.12)],.004,'Metal',p)
 box('LanternBeltLoop',(x,y+.03,.9),(.03,.025,.105),'Leather',p,.007)


def build(index):
 global mats
 key=['A','B','C'][index];mats=material_set(key,PALETTES[index]); root,hips,torso,head=body(key)
 scarf(torso)
 if index==0:
  cape('LongWayfarerCape',torso,.88,.445,.075);hood(head);satchel(torso,-.255,.80)
  # Organic stitched sprigs, enough to distinguish the default silhouette up close.
  for sign in [-1,1]:
   line('EmbroideredStem'+str(sign),[(sign*.24,-.205,1.23),(sign*.30,-.233,1.16),(sign*.35,-.255,1.09)],.002,'Thread',torso)
   for j in range(3):
    xx=sign*(.25+.032*j);zz=1.22-.052*j
    line('LeafStitch'+str(sign)+str(j),[(xx,-.234,zz),(xx+sign*.019,-.239,zz+.014)],.002,'Thread',torso)
 elif index==1:
  cape('ShortScavengerMantle',torso,1.12,.362,.045);floppy_hat(head);satchel(torso,-.255,.8)
  # A second pouch and asymmetric woven patches give this design a more rugged rhythm.
  box('UtilityPouch',(.235,-.15,.835),(.12,.11,.15),'Leather',hips,.022)
  box('UtilityFlap',(.235,-.212,.875),(.13,.016,.07),'Edge',hips,.012)
  for sign,z in [(-1,.54),(1,.63)]:
   x=sign*.19
   patch=[(x-.065,-.147,z-.05),(x+.055,-.155,z-.06),(x+.073,-.151,z+.026),(x+.025,-.147,z+.075),(x-.065,-.147,z+.057)]
   mesh('WovenRepair'+str(sign),patch,[tuple(range(5))],'Edge',hips,True,.005,.004)
   for j in range(3):line('RepairStitch'+str(sign)+str(j),[(x-.045+j*.035,-.162,z+.05),(x-.031+j*.035,-.162,z+.06)],.0018,'Thread',hips)
 else:
  cape('ArtisanShoulderCape',torso,1.08,.382,.055);beret(head);satchel(torso,-.245,.82,True);lantern(hips)
  # Standing linen collar and small brass cape fastening.
  rings('StandingCollar',[(1.32,.154,.124,0,0),(1.375,.15,.12,0,0),(1.405,.143,.114,0,0)],'Shirt',torso,n=56)
  ball('CapeClasp',(.075,-.197,1.323),(.018,.008,.018),'Metal',torso)
 # Merge within a joint and material, preserving the shared attachment hierarchy.
 groups={}
 for obj in root.children_recursive:
  if obj.type=='MESH':groups.setdefault((obj.parent,obj.data.materials[0]),[]).append(obj)
 for group in groups.values():
  if len(group)<2:continue
  bpy.ops.object.select_all(action='DESELECT')
  for obj in group:obj.select_set(True)
  bpy.context.view_layer.objects.active=group[0];bpy.ops.object.join()
 for obj in [root]+list(root.children_recursive):obj.name=key+'_'+obj.name.split('.')[0]
 print('BUILT_'+key,flush=True)
 return root,dict(mats)


def bake_and_export(root,materials,key):
 meshes=[o for o in root.children_recursive if o.type=='MESH']
 bpy.ops.object.select_all(action='DESELECT')
 for o in meshes:o.select_set(True)
 bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
 bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.004);bpy.ops.object.mode_set(mode='OBJECT')
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=4
 scene.render.bake.use_clear=False;scene.render.bake.margin=8;scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
 images={}
 for kind,label in [('DIFFUSE','Color'),('NORMAL','Normal')]:
  image=bpy.data.images.new(key+'_'+label,2048,2048,alpha=False)
  if kind=='NORMAL':image.colorspace_settings.name='Non-Color';image.generated_color=(.5,.5,1,1)
  for mat in materials.values():
   node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;mat.node_tree.nodes.active=node
  bpy.ops.object.bake(type=kind)
  image.filepath_raw=str(OUT/'textures'/f'{key}_{label}.png');image.file_format='PNG';image.save();images[label]=image
  print('BAKED_'+key+'_'+label,flush=True)
 for name,mat in materials.items():
  nodes=mat.node_tree.nodes;links=mat.node_tree.links;bs=nodes.get('Principled BSDF')
  tex=nodes.new('ShaderNodeTexImage');tex.image=images['Color'];links.new(tex.outputs['Color'],bs.inputs['Base Color'])
  if name!='Skin':
   texn=nodes.new('ShaderNodeTexImage');texn.image=images['Normal'];normal=nodes.new('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.65
   links.new(texn.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bs.inputs['Normal'])
 bpy.ops.object.select_all(action='DESELECT')
 for o in [root]+list(root.children_recursive):o.select_set(True)
 bpy.context.view_layer.objects.active=root
 bpy.ops.export_scene.gltf(filepath=str(OUT/f'{key}.glb'),export_format='GLB',use_selection=True,export_animations=False,export_yup=True)
 bpy.ops.export_scene.fbx(filepath=str(OUT/f'{key}.fbx'),use_selection=True,object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,path_mode='COPY',embed_textures=True)
 # Minimal dependency-free viewer geometry. UVs and textures match the exported models.
 data=[]
 for obj in meshes:
  obj.data.calc_loop_triangles();normal_matrix=obj.matrix_world.to_3x3().inverted().transposed();uv=obj.data.uv_layers.active.data
  for triangle in obj.data.loop_triangles:
   poly=obj.data.polygons[triangle.polygon_index]
   for loop_index in triangle.loops:
    loop=obj.data.loops[loop_index];vertex=obj.data.vertices[loop.vertex_index]
    pos=obj.matrix_world @ vertex.co;normal=normal_matrix @ (vertex.normal if poly.use_smooth else poly.normal);normal.normalize();coord=uv[loop_index].uv
    data.extend((*pos,*normal,*coord))
 (OUT/f'{key}.bin').write_bytes(struct.pack('<'+'f'*len(data),*data))
 return len(data)//8

roots=[];counts=[]
for i in range(3):
 root,materials=build(i);roots.append(root);counts.append(bake_and_export(root,materials,'ABC'[i]))
# A real editable comparison scene. Models remain independent collections and GLB/FBX files.
for i,root in enumerate(roots):root.location.x=(i-1)*1.7
for i,title in enumerate(['A   POUTNIK','B   STOPAR','C   REMESLNIK']):
 bpy.ops.object.text_add(location=((i-1)*1.7-.40,-.05,2.22),rotation=(math.pi/2,0,0));t=bpy.context.object;t.name='Label_'+str(i);t.data.body=title;t.data.size=.12;t.data.extrude=.001
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.005));floor=bpy.context.object;floor.name='StudioFloor';floor.data.materials.append(mats['Pants'])
def aim(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
for loc,power,size in [((0,-4,6),1100,6),((-5,-1,3),650,5),((3,4,5),1200,5)]:
 bpy.ops.object.light_add(type='AREA',location=loc);l=bpy.context.object;l.data.energy=power;l.data.shape='DISK';l.data.size=size;aim(l,(0,0,1))
bpy.ops.object.camera_add(location=(3,-10,3.7));camera=bpy.context.object;aim(camera,(0,0,1.05));camera.data.type='ORTHO';camera.data.ortho_scale=6.1;bpy.context.scene.camera=camera
bpy.context.scene.world.color=(.19,.19,.19)
for screen in bpy.data.screens:
 for area in screen.areas:
  if area.type=='VIEW_3D':
   area.spaces.active.shading.type='MATERIAL';area.spaces.active.overlay.show_overlays=False
   area.spaces.active.region_3d.view_location=(0,0,1.05);area.spaces.active.region_3d.view_distance=7.5
   area.spaces.active.region_3d.view_rotation=camera.rotation_euler.to_quaternion();area.spaces.active.region_3d.view_perspective='PERSP'
bpy.ops.object.select_all(action='DESELECT')
for root in roots:root.select_set(True)
bpy.context.view_layer.objects.active=roots[0]
bpy.context.preferences.filepaths.save_version=0
# Pack source textures for a portable .blend, in addition to the self-contained GLBs.
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Everlost_Character_Proposals.blend'))
(OUT/'models.json').write_text(json.dumps([
 {'id':k,'title':t,'description':d,'vertices':n,'file':k+'.bin','color':'textures/'+k+'_Color.png','normal':'textures/'+k+'_Normal.png'}
 for k,t,d,n in zip('ABC',['Poutník','Stopař','Řemeslník'],['Lesní kapuce, asymetrický plášť a jemné vyšívání.','Měkký klobouk, vínový přehoz a obnošená kůže.','Modrý krátký plášť, čepice a měděná lucerna.'],counts)],ensure_ascii=False))
print('PROPOSALS_COMPLETE '+str(counts),flush=True)
