"""Everlost traveler: rounded, short-legged character modeled from the supplied concept.
Run with Blender 5.x. Regenerates the editable source, game FBX and studio views.
"""
import bpy, math, os, random
from mathutils import Vector
random.seed(71)
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__), '../..'))
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
palette={
 'Linen':(.69,.56,.39,1), 'Leather':(.30,.185,.10,1),
 'Hat':(.40,.26,.145,1), 'Dark':(.235,.205,.175,1),
 'Scarf':(.60,.235,.12,1), 'Skin':(.90,.865,.77,1),
 'Brass':(.65,.435,.205,1), 'Patch':(.37,.265,.17,1),
 'Thread':(.56,.405,.25,1), 'Fold':(.56,.435,.295,1),
 'HatBand':(.255,.205,.16,1)}
mats={}
for name,c in palette.items():
 m=bpy.data.materials.new(name); m.diffuse_color=c; m.use_nodes=True
 bsdf=m.node_tree.nodes['Principled BSDF']; bsdf.inputs['Base Color'].default_value=tuple(v**2.2 for v in c[:3])+(1,)
 bsdf.inputs['Roughness'].default_value=.85
 if name=='Brass': bsdf.inputs['Metallic'].default_value=.45; bsdf.inputs['Roughness'].default_value=.55
 if name not in ('Skin','Brass'):
  nodes=m.node_tree.nodes; links=m.node_tree.links
  coord=nodes.new('ShaderNodeNewGeometry')
  broad=nodes.new('ShaderNodeTexNoise'); broad.inputs['Scale'].default_value=16 if name in ('Hat','Leather','HatBand') else 24
  broad.inputs['Detail'].default_value=3.3; broad.inputs['Roughness'].default_value=.72
  links.new(coord.outputs['Position'],broad.inputs['Vector'])
  stain=nodes.new('ShaderNodeValToRGB'); stain.color_ramp.elements[0].position=.18; stain.color_ramp.elements[0].color=(.48,.43,.38,1)
  stain.color_ramp.elements[1].position=.78; stain.color_ramp.elements[1].color=(1.13,1.08,1.0,1)
  links.new(broad.outputs['Fac'],stain.inputs[0])
  mix=nodes.new('ShaderNodeMixRGB'); mix.blend_type='MULTIPLY'; mix.inputs[0].default_value=1
  mix.inputs[1].default_value=tuple(v**2.2 for v in c[:3])+(1,)
  links.new(stain.outputs['Color'],mix.inputs[2]); links.new(mix.outputs[0],bsdf.inputs['Base Color'])
  fine=nodes.new('ShaderNodeTexNoise'); fine.inputs['Scale'].default_value=210 if name in ('Linen','Scarf','Dark','Fold') else 120
  fine.inputs['Detail'].default_value=2; fine.inputs['Roughness'].default_value=.65
  links.new(coord.outputs['Position'],fine.inputs['Vector'])
  bump=nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.28; bump.inputs['Distance'].default_value=.004
  links.new(fine.outputs['Fac'],bump.inputs['Height']); links.new(bump.outputs['Normal'],bsdf.inputs['Normal'])
 elif name=='Skin': bsdf.inputs['Subsurface Weight'].default_value=.035
 mats[name]=m

def joint(name,loc,p=None):
 o=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(o); o.location=loc
 bpy.context.view_layer.update()
 if p: keep=o.matrix_world.copy(); o.parent=p; o.matrix_world=keep
 bpy.context.view_layer.update(); return o

def parent(o,p):
 bpy.context.view_layer.update()
 if p: keep=o.matrix_world.copy(); o.parent=p; o.matrix_world=keep
 return o

def finish(o,name,mat,p,smooth=True):
 o.name=name; o.data.materials.append(mats[mat])
 if smooth:
  for polygon in o.data.polygons: polygon.use_smooth=True
 return parent(o,p)

def apply(o,modifier):
 bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=modifier.name)

def ball(name,loc,scale,mat,p=None):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=20,location=loc)
 o=bpy.context.object; o.scale=scale; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 return finish(o,name,mat,p)

def box(name,loc,scale,mat,p=None,bevel=.025):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.scale=scale
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel:
  mod=o.modifiers.new('Rounded worn corners','BEVEL'); mod.width=bevel; mod.segments=4; apply(o,mod)
 finish(o,name,mat,p)
 mod=o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL'); mod.keep_sharp=True; apply(o,mod)
 return o

def mesh(name,verts,faces,mat,p,smooth=True,thickness=0,bevel=0):
 data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
 o=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(o); finish(o,name,mat,p,smooth)
 if thickness:
  mod=o.modifiers.new('Cloth thickness','SOLIDIFY'); mod.thickness=thickness; apply(o,mod)
 if bevel:
  mod=o.modifiers.new('Soft fabric edges','BEVEL'); mod.width=bevel; mod.segments=3; apply(o,mod)
 return o

def rings(name,levels,mat,p,n=40,torn=False,fold=0):
 verts=[]
 for k,(z,rx,ry,cx,cy) in enumerate(levels):
  for i in range(n):
   a=math.tau*i/n
   wave=1+fold*(math.sin(7*a+.3)+.5*math.sin(11*a+1))
   dz=0
   if torn and k==0:
    dz=.009*math.sin(5*a)+.008*math.sin(11*a+.5)
    if i%9==2: dz+=.025
   verts.append((cx+rx*math.cos(a)*wave,cy+ry*math.sin(a)*wave,z+dz))
 faces=[tuple(range(n-1,-1,-1))]
 for k in range(len(levels)-1):
  for i in range(n): faces.append((k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i))
 faces.append(tuple((len(levels)-1)*n+i for i in range(n)))
 o=mesh(name,verts,faces,mat,p)
 # End caps are flat; longitudinal cloth and skin are smooth.
 o.data.polygons[0].use_smooth=False; o.data.polygons[-1].use_smooth=False
 return o

def tube(name,points,radius,mat,p):
 curve=bpy.data.curves.new(name,'CURVE'); curve.dimensions='3D'; curve.resolution_u=8
 spl=curve.splines.new('BEZIER'); spl.bezier_points.add(len(points)-1)
 for point,co in zip(spl.bezier_points,points): point.co=co; point.handle_left_type='AUTO'; point.handle_right_type='AUTO'
 curve.bevel_depth=radius; curve.bevel_resolution=2
 o=bpy.data.objects.new(name,curve); bpy.context.collection.objects.link(o)
 bpy.context.view_layer.objects.active=o; o.select_set(True); bpy.ops.object.convert(target='MESH'); o=bpy.context.object
 o.select_set(False); return finish(o,name,mat,p)

def strap(name,a,b,width,depth,mat,p):
 mid=(Vector(a)+Vector(b))*.5
 o=box(name,mid,(width,depth,(Vector(b)-Vector(a)).length),mat,None,.007)
 o.rotation_euler=(Vector(b)-Vector(a)).to_track_quat('Z','Y').to_euler(); return parent(o,p)

def ribbon(name,rows,mat,p,width=.08):
 # Rows: center x/y/z and local half-width. Rounded solid edges read as cloth, not boxes.
 verts=[]
 for x,y,z,w in rows: verts.extend([(x-w*.5,y,z),(x+w*.5,y,z)])
 faces=[(i*2,i*2+1,(i+1)*2+1,(i+1)*2) for i in range(len(rows)-1)]
 return mesh(name,verts,faces,mat,p,True,.012,.006)

def buckle(name,x,y,z,w,h,p):
 for dx in [-w*.5,w*.5]: box(name+'Side',(x+dx,y,z),(.012,.025,h),'Brass',p,.004)
 for dz in [-h*.5,h*.5]: box(name+'Bar',(x,y,z+dz),(w,.025,.012),'Brass',p,.004)
 box(name+'Tongue',(x,y-.015,z),(.009,.01,h*.65),'Brass',p,.003)

# The pelvis sits lower: short, sturdy legs and a rounded, larger head.
root=joint('Traveler',(0,0,0)); hips=joint('Hips',(0,0,.735),root)
torso=joint('Torso',(0,0,1.04),hips); head=joint('Head',(0,0,1.48),torso)
rings('Tunic',[(.64,.30,.18,0,0),(.72,.315,.19,0,0),(.86,.265,.18,0,0),(.96,.26,.178,0,0),(1.10,.295,.19,0,0),(1.28,.31,.19,0,0),(1.39,.235,.145,0,0),(1.43,.16,.115,0,0)],'Linen',torso,torn=True,fold=.016)
ball('Face',(0,-.035,1.625),(.265,.24,.275),'Skin',head)
# Soft charcoal shoulder mantle with a thin, uneven frayed hem.
rings('ShoulderCape',[(1.21,.375,.208,0,.025),(1.26,.385,.214,0,.02),(1.35,.33,.196,0,.02),(1.43,.215,.14,0,.02),(1.46,.16,.12,0,.015)],'Dark',torso,n=48,torn=True,fold=.014)
# Folded scarf wraps; the face remains a single smooth featureless surface.
rings('ScarfLowerFold',[(1.385,.175,.14,0,-.018),(1.41,.203,.161,0,-.02),(1.445,.20,.16,0,-.02),(1.46,.165,.13,0,-.01)],'Scarf',torso)
rings('ScarfUpperFold',[(1.43,.172,.14,0,-.012),(1.455,.212,.17,0,-.015),(1.485,.20,.16,0,-.01),(1.51,.16,.125,0,0)],'Scarf',torso)
ball('ScarfKnot',(.105,-.163,1.425),(.083,.055,.056),'Scarf',torso)
ribbon('ScarfFrontTail',[(.105,-.194,1.43,.095),(.12,-.206,1.36,.09),(.13,-.208,1.27,.075),(.145,-.207,1.18,.057),(.17,-.203,1.145,.04)],'Scarf',torso)
ribbon('ScarfSecondTail',[(.16,-.173,1.435,.067),(.20,-.18,1.36,.065),(.205,-.183,1.29,.055),(.19,-.192,1.22,.045)],'Scarf',torso)
ribbon('ScarfBackTail',[(.09,.16,1.46,.095),(.11,.20,1.35,.09),(.15,.21,1.23,.08),(.15,.215,1.12,.068),(.18,.213,1.08,.05)],'Scarf',torso)
# Wrap sits under the chin and visibly folds over the charcoal shoulder mantle.
for obj in list(bpy.context.scene.objects):
 if obj.type!='MESH' or not obj.name.startswith('Scarf'): continue
 for vertex in obj.data.vertices:
  world=obj.matrix_world @ vertex.co
  if obj.name.startswith('ScarfLower') or obj.name.startswith('ScarfUpper'):
   world.x*=1.09; world.y=world.y*1.16-.008; world.z-=.075
  else: world.z-=.055
  vertex.co=obj.matrix_world.inverted() @ world
# Broad folded bandana front, tucked under the knot rather than a narrow necktie.
ribbon('ScarfChestFold',[(.005,-.209,1.405,.34),(.025,-.225,1.355,.315),(.045,-.231,1.30,.20),(.07,-.23,1.265,.07)],'Scarf',torso)
# Wavy cloth brim: a thin layered edge rather than a rigid flat disc.
n=144; verts=[]
for k,(rx,ry,z) in enumerate([(.225,.205,1.82),(.33,.285,1.80),(.47,.385,1.785),(.475,.39,1.799),(.33,.285,1.815),(.225,.205,1.836)]):
 for i in range(n):
  a=i*math.tau/n
  edge=k in (2,3)
  notch=.009*math.sin(13*a+.6) if edge else 0
  drape=(.026*math.sin(3*a+.5)+.018*math.cos(2*a)-.045*max(0,-math.sin(a)))*(1 if edge else .25)
  verts.append(((rx+notch)*math.cos(a),(ry+notch)*math.sin(a),z+drape))
faces=[]
for k in range(6):
 for i in range(n): faces.append((k*n+i,k*n+(i+1)%n,((k+1)%6)*n+(i+1)%n,((k+1)%6)*n+i))
mesh('HatBrim',verts,faces,'Hat',head)
rings('HatCrown',[(1.825,.235,.207,0,0),(1.88,.231,.20,-.004,.005),(1.96,.205,.18,-.018,.01),(2.035,.155,.14,-.046,.018),(2.09,.105,.09,-.07,.018),(2.11,.065,.06,-.08,.01)],'Hat',head,n=64,fold=.045)
rings('HatBand',[(1.846,.257,.230,0,0),(1.865,.255,.228,-.002,.003),(1.903,.247,.222,-.009,.005)],'HatBand',head)
# Raised seam around the brim and two subtle repaired tears in the crown.
points=[]
for i in range(73):
 a=i*math.tau/72; points.append((.473*math.cos(a),.388*math.sin(a),1.799+.026*math.sin(3*a+.5)+.018*math.cos(2*a)-.045*max(0,-math.sin(a))))
tube('HatEdgeSeam',points,.003,'Leather',head)
for i in range(5):
 x=.186+i*.004; z=1.88+i*.013
 tube('HatRepair'+str(i),[(x-.012,-.128,z-.003),(x,-.135,z),(x+.01,-.13,z+.004)],.0024,'Thread',head)
# Belt follows the rounded waist; buckle is an actual open frame.
rings('Belt',[(.865,.285,.197,0,0),(.875,.29,.20,0,0),(.932,.281,.192,0,0),(.94,.274,.19,0,0)],'Leather',hips)
buckle('WaistBuckle',.015,-.205,.905,.096,.078,hips)
for i in range(5): ball('BeltHole'+str(i),(.10+i*.023,-.195,.905),(.003,.002,.003),'Dark',hips)
strap('ChestStrap',(-.235,-.195,1.385),(.25,-.206,.85),.066,.022,'Leather',torso)
buckle('StrapBuckle',.075,-.227,1.075,.064,.073,torso)
for side,sign in [('L',-1),('R',1)]:
 x=sign*.26
 leg=joint('Leg'+side,(sign*.205,0,.735),hips); knee=joint('Knee'+side,(x,0,.405),leg)
 rings('TrousersUpper'+side,[(.385,.143,.136,x,0),(.44,.157,.15,x,0),(.54,.165,.16,x,.004),(.66,.16,.15,sign*.23,0),(.76,.13,.13,sign*.205,0)],'Dark',leg,fold=.014)
 rings('TrousersLower'+side,[(.25,.125,.12,x,0),(.30,.14,.135,x,0),(.38,.145,.14,x,0),(.435,.15,.145,x,0)],'Dark',knee,fold=.014)
 # A few rounded wrinkles and an irregular knee repair, not square armour plates.
 patch=[(x-.085,-.139,.36),(x+.055,-.149,.35),(x+.075,-.15,.425),(x+.045,-.145,.485),(x-.065,-.14,.475),(x-.09,-.14,.42)]
 patch=[(px+sign*.008,py,pz+(.035 if side=='L' else .11)) for px,py,pz in patch]
 # Conform the repair to the curved trousers so its centre cannot disappear inside them.
 patch.append((sum(v[0] for v in patch)/6,sum(v[1] for v in patch)/6,sum(v[2] for v in patch)/6))
 patch_obj=mesh('KneePatch'+side,patch,[(6,i,(i+1)%6) for i in range(6)],'Patch',leg)
 mod=patch_obj.modifiers.new('Curved textile repair','SUBSURF'); mod.subdivision_type='SIMPLE'; mod.levels=2; apply(patch_obj,mod)
 for vertex in patch_obj.data.vertices:
  world=patch_obj.matrix_world @ vertex.co
  levels=[(.385,.143,.136,x,0),(.44,.157,.15,x,0),(.54,.165,.16,x,.004),(.66,.16,.15,sign*.23,0),(.76,.13,.13,sign*.205,0)]
  for low,high in zip(levels,levels[1:]):
   if low[0]<=world.z<=high[0]:
    t=(world.z-low[0])/(high[0]-low[0]); rx=low[1]*(1-t)+high[1]*t; ry=low[2]*(1-t)+high[2]*t; cx=low[3]*(1-t)+high[3]*t; cy=low[4]*(1-t)+high[4]*t
    world.y=cy-ry*math.sqrt(max(.01,1-((world.x-cx)/rx)**2))-.009; break
  vertex.co=patch_obj.matrix_world.inverted() @ world
 mod=patch_obj.modifiers.new('Sewn patch edge','SOLIDIFY'); mod.thickness=.003; apply(patch_obj,mod)
 for j in range(3):
  xx=x-.06+j*.04
  tube('KneeStitch'+side+str(j),[(xx,-.172,.458+(.035 if side=='L' else .11)),(xx+.012,-.172,.472+(.035 if side=='L' else .11))],.002,'Thread',leg)
 # Rounded leather boot, broad toe, separate flexible shaft, layered sole and cuff.
 box('BootSole'+side,(x,-.065,.041),(.282,.405,.07),'Dark',knee,.032)
 box('BootToe'+side,(x,-.085,.135),(.275,.385,.18),'Leather',knee,.065)
 rings('BootShaft'+side,[(.08,.127,.122,x,.018),(.16,.128,.125,x,.018),(.245,.128,.129,x,.018),(.28,.137,.139,x,.015)],'Leather',knee)
 rings('BootCuff'+side,[(.245,.142,.143,x,.015),(.268,.151,.151,x,.015),(.305,.148,.147,x,.013),(.315,.137,.137,x,.01)],'Hat',knee)
 for j in range(3):
  yy=-.19+j*.037
  tube('BootLace'+side+str(j),[(x-.058,yy-.008,.216),(x,yy,.227),(x+.058,yy+.008,.216)],.005,'Hat',knee)
 box('BootSideStrap'+side,(x+sign*.14,-.03,.267),(.026,.13,.042),'Leather',knee,.01)
 box('BootSideBuckle'+side,(x+sign*.156,-.065,.267),(.012,.04,.04),'Brass',knee,.008)
 # Relaxed shoulders, short soft arms and rounded mitten-like fingers.
 arm=joint('Arm'+side,(sign*.315,0,1.335),torso)
 elbow=joint('Elbow'+side,(sign*.405,-.006,1.10),arm)
 hand=joint('Hand'+side,(sign*.435,-.02,.91),elbow)
 ball('UpperArm'+side,(sign*.35,-.002,1.225),(.105,.11,.14),'Linen',arm)
 rings('Sleeve'+side,[(1.135,.117,.12,sign*.38,-.002),(1.18,.124,.125,sign*.367,0),(1.29,.114,.12,sign*.325,0),(1.345,.095,.105,sign*.315,0)],'Linen',arm,torn=True,fold=.012)
 ball('Forearm'+side,(sign*.424,-.009,1.035),(.084,.088,.13),'Skin',elbow)
 rings('WristWrap'+side,[(.938,.093,.091,sign*.434,-.016),(.955,.10,.096,sign*.432,-.014),(1.02,.095,.092,sign*.423,-.009),(1.035,.088,.085,sign*.42,-.007)],'Leather',elbow)
 box('WrapStrap'+side,(sign*.429,-.103,.984),(.177,.018,.034),'Hat',elbow,.008)
 # Fuse palm, finger tips and thumb so hands have a continuous rounded silhouette.
 hand_parts=[ball('Palm'+side,(sign*.438,-.018,.881),(.087,.073,.095),'Skin',hand),
             ball('Thumb'+side,(sign*.371,-.067,.891),(.039,.043,.063),'Skin',hand)]
 for j in range(3): hand_parts.append(ball('Finger'+side+str(j),(sign*(.397+j*.035),-.025,.827),(.027,.061,.039),'Skin',hand))
 bpy.ops.object.select_all(action='DESELECT')
 for o in hand_parts: o.select_set(True)
 bpy.context.view_layer.objects.active=hand_parts[0]; bpy.ops.object.join(); o=bpy.context.object
 mod=o.modifiers.new('Continuous mitten surface','REMESH'); mod.mode='VOXEL'; mod.voxel_size=.006; mod.use_smooth_shade=True; apply(o,mod)
 mod=o.modifiers.new('Gentle hand smoothing','SMOOTH'); mod.factor=.45; mod.iterations=3; apply(o,mod)
 for face in o.data.polygons: face.use_smooth=True
# Rounded satchel with a curved flap, fine seams and a hanging clasp.
box('Satchel',(-.274,-.18,.795),(.235,.156,.235),'Leather',hips,.038)
flap=[(-.398,-.257,.925),(-.154,-.257,.925),(-.154,-.276,.846),(-.18,-.284,.818),(-.365,-.284,.818),(-.395,-.276,.846)]
mesh('SatchelFlap',flap,[tuple(range(6))],'Hat',hips,True,.018,.012)
tube('SatchelSeam',[(-.375,-.264,.87),(-.37,-.268,.715),(-.34,-.269,.695),(-.205,-.269,.695),(-.184,-.263,.715),(-.18,-.263,.81)],.0025,'Thread',hips)
box('SatchelClasp',(-.274,-.295,.825),(.036,.023,.073),'Leather',hips,.008)
box('SatchelClaspButton',(-.274,-.309,.814),(.019,.01,.023),'Brass',hips,.005)
for i in range(6):
 x=-.374+i*.038
 tube('BagFlapStitch'+str(i),[(x,-.295,.838),(x+.012,-.295,.838)],.0018,'Thread',hips)
# Soft sewn shirt repair and short folds at the waist and shoulders.
patch=[(.10,-.19,1.005),(.205,-.187,1.016),(.218,-.184,1.115),(.124,-.193,1.124)]
mesh('TunicPatch',patch,[(0,1,2,3)],'Fold',torso,True,.004,.005)
for i in range(4):
 xx=.126+i*.024
 tube('TunicStitch'+str(i),[(xx,-.202,1.11),(xx+.008,-.203,1.126)],.0018,'Thread',torso)
for side in [-1,1]:
 tube('WaistFold'+str(side),[(side*.12,-.175,.955),(side*.16,-.19,1.01),(side*.18,-.185,1.07)],.004,'Fold',torso)
# Tailored hanging tunic panels; each has its own curved fold and irregular worn tip.
for panel in range(14):
 angle=math.tau*panel/14; spread=.56+.035*math.sin(panel*2.1)
 points=[]
 for row in range(5):
  t=row/4; radius=.278+.042*t; z=.893-(.225+.028*math.sin(panel*1.7))*t
  for col in range(5):
   u=col/4; a=angle+(u-.5)*spread
   rr=radius+.006+.012*math.sin(u*math.pi)*math.sin(t*math.pi)
   zz=z
   if row==4: zz+=.013*math.sin(panel*2.7)+(.027 if col==2 and panel%3==0 else 0)
   points.append((rr*math.cos(a),rr*.65*math.sin(a),zz))
 faces=[]
 for row in range(4):
  for col in range(4):
   i=row*5+col; faces.append((i,i+1,i+6,i+5))
 mesh('TunicHemPanel'+str(panel),points,faces,'Linen',torso,True,.004,.003)
# Open the front of the mantle into two soft lapels, as on the concept.
for obj in list(bpy.context.scene.objects):
 if obj.type!='MESH': continue
 if obj.name.startswith('ShoulderCape'):
  for vertex in obj.data.vertices:
   world=obj.matrix_world @ vertex.co
   a=math.atan2(world.y-.025,world.x)
   front=math.exp(-((math.atan2(math.sin(a+math.pi/2),math.cos(a+math.pi/2)))/.43)**2)
   if world.z<1.36: world.z+=.16*front*min(1,(1.36-world.z)/.09)
   vertex.co=obj.matrix_world.inverted() @ world
 if obj.name.startswith(('Tunic','ShoulderCape','Sleeve','HatCrown')):
  mod=obj.modifiers.new('Dense cloth surface','SUBSURF'); mod.subdivision_type='SIMPLE'; mod.levels=1; apply(obj,mod)
  for vertex in obj.data.vertices:
   world=obj.matrix_world @ vertex.co
   center_x=0
   if obj.name.startswith('Sleeve'): center_x=.35 if world.x>0 else -.35
   a=math.atan2(world.y,world.x-center_x)
   amp=.005 if obj.name.startswith('HatCrown') else .0035
   fold=amp*math.sin(a*9+world.z*15)+amp*.55*math.sin(a*17-world.z*22)
   world.x+=math.cos(a)*fold; world.y+=math.sin(a)*fold
   vertex.co=obj.matrix_world.inverted() @ world
# Stitching and loose fibres along leather edges and garment repairs.
for side in [-1,1]:
 for stitch in range(6):
  x=side*(.18+stitch*.017); z=1.33-stitch*.006
  tube('ShoulderLacing'+str(side)+'_'+str(stitch),[(x-.006,-.184,z-.008),(x+.006,-.187,z+.008)],.002,'Thread',torso)
for i in range(18):
 a=i*math.tau/18
 if math.sin(a)>-.2: continue
 x=.294*math.cos(a); y=.194*math.sin(a); z=.90
 tube('BeltEdgeStitch'+str(i),[(x-.004,y-.004,z+.022),(x+.004,y-.004,z+.022)],.0018,'Thread',hips)
# A sewn crown repair with an asymmetric leather patch.
mesh('HatCrownRepair',[(.165,-.142,1.94),(.193,-.134,1.97),(.158,-.15,2.012),(.129,-.163,1.984)],[(0,1,2,3)],'Patch',head,True,.005,.003)
for i in range(4):
 z=1.947+i*.014
 tube('CrownPatchThread'+str(i),[(.171,-.15,z),(.187,-.14,z+.005)],.0018,'Thread',head)
# Consolidate only parts belonging to the same animated joint/material.
groups={}
for obj in list(bpy.context.scene.objects):
 if obj.type=='MESH': groups.setdefault((obj.parent,obj.data.materials[0].name),[]).append(obj)
for objects in groups.values():
 if len(objects)<2: continue
 bpy.ops.object.select_all(action='DESELECT')
 for obj in objects: obj.select_set(True)
 bpy.context.view_layer.objects.active=objects[0]; bpy.ops.object.join()
# One non-overlapping UV layout shared by the whole character.
model_meshes=[o for o in root.children_recursive if o.type=='MESH']
bpy.ops.object.select_all(action='DESELECT')
for obj in model_meshes: obj.select_set(True)
bpy.context.view_layer.objects.active=model_meshes[0]
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.004)
bpy.ops.object.mode_set(mode='OBJECT')
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=8
scene.render.bake.use_clear=False; scene.render.bake.margin=8
scene.render.bake.use_pass_direct=False; scene.render.bake.use_pass_indirect=False; scene.render.bake.use_pass_color=True
for kind,label in [('DIFFUSE','BaseColor'),('NORMAL','Normal')]:
 image=bpy.data.images.new('Traveler'+label,width=2048,height=2048,alpha=False)
 if kind=='NORMAL': image.colorspace_settings.name='Non-Color'; image.generated_color=(.5,.5,1,1)
 else: image.generated_color=(.1,.08,.05,1)
 for mat in mats.values():
  node=mat.node_tree.nodes.new('ShaderNodeTexImage'); node.name='Baked'+label; node.image=image
  mat.node_tree.nodes.active=node
 bpy.ops.object.bake(type=kind)
 image.filepath_raw=os.path.join(ROOT,'Assets/Resources/Characters/Traveler'+label+'.png'); image.file_format='PNG'; image.save()
 print('BAKED_'+label,flush=True)
bpy.ops.object.select_all(action='DESELECT')
for obj in [root]+list(root.children_recursive): obj.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT,'ArtSource/Traveler/Traveler.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(ROOT,'Assets/Resources/Characters/Traveler.fbx'),use_selection=True,object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,use_mesh_modifiers=True)
# Studio is deliberately excluded from both source model and FBX.
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,.001)); bpy.context.object.data.materials.append(mats['Dark'])
def aim(o,p): o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3,-7,2.7)); cam=bpy.context.object; aim(cam,(0,0,1.04)); cam.data.type='ORTHO'; cam.data.ortho_scale=2.55; bpy.context.scene.camera=cam
for loc,power,size in [((1,-3,5),420,4),((-3,-1,2),180,3),((1,3,4),500,3)]:
 bpy.ops.object.light_add(type='AREA',location=loc); light=bpy.context.object; light.data.energy=power; light.data.shape='DISK'; light.data.size=size; aim(light,(0,0,1))
s=bpy.context.scene; s.render.engine='CYCLES'; s.cycles.samples=32; s.cycles.use_denoising=True; s.world.color=(.16,.16,.16)
s.render.resolution_x=850; s.render.resolution_y=1000; s.render.resolution_percentage=100
s.render.filepath=os.path.join(ROOT,'ArtSource/Traveler/preview.png'); bpy.ops.render.render(write_still=True)
cam.location=(0,-7,2.05); aim(cam,(0,0,1.04)); s.render.filepath=os.path.join(ROOT,'ArtSource/Traveler/front.png'); bpy.ops.render.render(write_still=True)
cam.location=(3,6,2.5); aim(cam,(0,0,1.04)); s.render.filepath=os.path.join(ROOT,'ArtSource/Traveler/back.png'); bpy.ops.render.render(write_still=True)
print('TRAVELER_COMPLETE',flush=True)
