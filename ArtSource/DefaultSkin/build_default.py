"""Author one fixed default asset in the style of the user-supplied everlost-3d.zip.
No seeds, rarity rolls, random choices or runtime character generation.
Coordinates and primitive proportions follow the supplied model.js.
"""
import bpy,math,json,struct
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parent
PROJECT=OUT.parents[1]
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
COLORS={'DefaultSkin':'f4f3e9','DefaultCloth':'b1a083','DefaultCape':'66755a','DefaultAccent':'a45538','DefaultLeather':'594638','DefaultSole':'393932','DefaultMetal':'bba475'}
materials={}
for name,value in COLORS.items():
 c=tuple(int(value[i:i+2],16)/255 for i in (0,2,4));m=bpy.data.materials.new(name);m.diffuse_color=c+(1,);m.use_nodes=True
 bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=tuple(v**2.2 for v in c)+(1,);bs.inputs['Roughness'].default_value=.9
 if name=='DefaultMetal':bs.inputs['Metallic'].default_value=.6;bs.inputs['Roughness'].default_value=.5
 materials[name]=m

def P(v):return Vector((v[0],-v[2],v[1]))
def joint(name,position,parent=None):
 o=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(o);o.location=P(position);bpy.context.view_layer.update()
 if parent:mw=o.matrix_world.copy();o.parent=parent;o.matrix_world=mw
 return o

def finish(o,name,mat,position,scale,parent,smooth=False):
 o.name=name;o.location=P(position);o.scale=(scale[0],scale[2],scale[1]);o.data.materials.append(materials[mat])
 for poly in o.data.polygons:poly.use_smooth=smooth
 bpy.context.view_layer.update();mw=o.matrix_world.copy();o.parent=parent;o.matrix_world=mw
 return o

def ico(name,mat,pos,scale,parent,detail=1):
 # Three.js PolyhedronGeometry detail n divides each edge into n+1 pieces.
 t=(1+math.sqrt(5))/2
 base=[(-1,t,0),(1,t,0),(-1,-t,0),(1,-t,0),(0,-1,t),(0,1,t),(0,-1,-t),(0,1,-t),(t,0,-1),(t,0,1),(-t,0,-1),(-t,0,1)]
 faces=[(0,11,5),(0,5,1),(0,1,7),(0,7,10),(0,10,11),(1,5,9),(5,11,4),(11,10,2),(10,7,6),(7,1,8),(3,9,4),(3,4,2),(3,2,6),(3,6,8),(3,8,9),(4,9,5),(2,4,11),(6,2,10),(8,6,7),(9,8,1)]
 vertices=[];triangles=[];n=detail+1
 for face in faces:
  a,b,c=[Vector(base[i]) for i in face];grid={}
  for i in range(n+1):
   for j in range(n+1-i):
    v=(a*(n-i-j)+b*i+c*j).normalized();grid[i,j]=len(vertices);vertices.append(tuple(P(v)))
  for i in range(n):
   for j in range(n-i):
    triangles.append((grid[i,j],grid[i+1,j],grid[i,j+1]))
    if j<n-i-1:triangles.append((grid[i+1,j],grid[i+1,j+1],grid[i,j+1]))
 data=bpy.data.meshes.new(name);data.from_pydata(vertices,[],triangles);data.update();o=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(o)
 # White hands use smooth sphere normals, clothes intentionally retain the supplied style.
 if mat=='DefaultSkin':
  # Weld the duplicated face edges so smooth normals really are continuous.
  bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.remove_doubles(threshold=.00001);bpy.ops.object.mode_set(mode='OBJECT');o.select_set(False)
 return finish(o,name,mat,pos,scale,parent,mat=='DefaultSkin')

def sphere(name,mat,pos,scale,parent,radius=1):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=48,ring_count=32,radius=radius);o=bpy.context.object
 return finish(o,name,mat,pos,scale,parent,True)

def box(name,mat,pos,size,parent):
 bpy.ops.mesh.primitive_cube_add(size=1);return finish(bpy.context.object,name,mat,pos,size,parent)

def cylinder(name,mat,pos,top,bottom,height,segments,parent,depth=1):
 bpy.ops.mesh.primitive_cone_add(vertices=segments,radius1=bottom,radius2=top,depth=height)
 return finish(bpy.context.object,name,mat,pos,(1,1,depth),parent)

def ring(name,mat,y,r,tube,parent,sx=1,sz=1,x=0,z=0):
 bpy.ops.mesh.primitive_torus_add(major_segments=24,minor_segments=5,major_radius=r,minor_radius=tube)
 return finish(bpy.context.object,name,mat,(x,y,z),(sx,1,sz),parent)

root=joint('DefaultWanderer',(0,0,0));hips=joint('Hips',(0,.86,0),root);torso=joint('Torso',(0,1.18,0),hips);head=joint('Head',(0,2.13,.015),torso)
sphere('Face','DefaultSkin',(0,2.13,.015),(1,1.03,.91),head,.67)
ico('Body','DefaultCloth',(0,1.18,0),(.46,.57,.32),torso,2)
for suffix,side in [('L',-1),('R',1)]:
 leg=joint('Leg'+suffix,(side*.235,.82,0),hips);knee=joint('Knee'+suffix,(side*.25,.40,0),leg)
 ico('Trousers'+suffix,'DefaultLeather',(side*.235,.53,0),(.185,.36,.2),leg,1)
 arm=joint('Arm'+suffix,(side*.46,1.50,0),torso);elbow=joint('Elbow'+suffix,(side*.56,1.13,0),arm);hand=joint('Hand'+suffix,(side*.59,.96,.04),elbow)
 sleeve=ico('Sleeve'+suffix,'DefaultCloth',(side*.5,1.23,0),(.19,.36,.21),arm,2)
 # Three.js rotation.z maps to rotation around Blender's vertical/depth-converted axis.
 sleeve.rotation_euler.y=-side*.23
 ico('HandMesh'+suffix,'DefaultSkin',(side*.59,.96,.04),(.16,.18,.16),hand,2)
 ring('WristBinding'+suffix,'DefaultLeather',1.08,.143,.025,elbow,x=side*.575,z=.025)
 ico('BootSole'+suffix,'DefaultSole',(side*.26,.095,.12),(.265,.095,.36),knee,1)
 ico('Boot'+suffix,'DefaultLeather',(side*.26,.245,.14),(.26,.23,.35),knee,2)
 cylinder('BootShaft'+suffix,'DefaultLeather',(side*.26,.4,0),.205,.22,.32,8,knee)
cylinder('Tunic','DefaultCloth',(0,1.09,0),.37,.47,.58,10,torso,.76)
ring('WaistBelt','DefaultLeather',1.13,.43,.045,hips,sz=.74)
box('BeltBuckle','DefaultMetal',(0,1.14,.345),(.13,.105,.04),hips)
box('Pouch','DefaultLeather',(.34,1.03,.26),(.18,.21,.10),hips)
box('PouchFlap','DefaultAccent',(.34,1.085,.32),(.19,.065,.035),hips)
# Fixed short moss-green cape. No alternate part selection occurs anywhere.
vertices=[];faces=[];rows=8;cols=20
for j in range(rows+1):
 v=j/rows
 for i in range(cols+1):
  a=.35+i/cols*(math.tau-.7);radius=.46+v*.15
  vertices.append(tuple(P((math.sin(a)*radius,1.65-v*.75+(.04*math.cos(i*2) if j==rows else 0),math.cos(a)*radius*.77-.04))))
for j in range(rows):
 for i in range(cols):
  a=j*(cols+1)+i;b=a+cols+1;faces.extend([(a,b,a+1),(b,b+1,a+1)])
mesh=bpy.data.meshes.new('StarterCape');mesh.from_pydata(vertices,[],faces);mesh.update();cape=bpy.data.objects.new('StarterCape',mesh);bpy.context.collection.objects.link(cape)
finish(cape,'StarterCape','DefaultCape',(0,0,0),(1,1,1),torso)
# Thin double-sided thickness makes the cape portable to FBX and glTF.
bpy.context.view_layer.objects.active=cape;cape.select_set(True);mod=cape.modifiers.new('Cape thickness','SOLIDIFY');mod.thickness=.008;bpy.ops.object.modifier_apply(modifier=mod.name)
ring('StarterScarf','DefaultAccent',1.68,.34,.12,torso,sz=.9)
tail=box('ScarfTail','DefaultAccent',(.21,1.44,.35),(.18,.42,.08),torso);tail.rotation_euler.y=.23
# Smaller starter boots: preserve ankle centres and ground contact while reducing bulk.
boot_meshes=[o for o in root.children_recursive if o.type=='MESH' and o.name.startswith(('BootSole','BootShaft','BootL','BootR'))]
boot_floor=min((o.matrix_world @ v.co).z for o in boot_meshes for v in o.data.vertices)
for obj in boot_meshes:
 side=-1 if obj.name.endswith('L') else 1
 inverse=obj.matrix_world.inverted()
 for vertex in obj.data.vertices:
  world=obj.matrix_world @ vertex.co
  world.x=side*.26+(world.x-side*.26)*.76
  world.y*=.74
  world.z=boot_floor+(world.z-boot_floor)*.86
  vertex.co=inverse @ world
 obj.data.update()
# Export one finished model; the fixed identity does not contain a seed.
root['skin_id']='everlost:default_wanderer';root['display_name']='První poutník';root['generated']=False
for mat in materials.values():mat.use_backface_culling=False
bpy.ops.object.select_all(action='DESELECT')
for obj in [root]+list(root.children_recursive):obj.select_set(True)
bpy.context.view_layer.objects.active=root
asset=PROJECT/'Assets/Resources/Characters/DefaultWanderer.fbx'
bpy.ops.export_scene.fbx(filepath=str(asset),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
bpy.ops.export_scene.gltf(filepath=str(OUT/'DefaultWanderer.glb'),export_format='GLB',use_selection=True,export_animations=False)
# Exact mesh data for the local interactive viewer, including per-material vertex colour.
data=[]
for obj in root.children_recursive:
 if obj.type!='MESH':continue
 obj.data.calc_loop_triangles();nm=obj.matrix_world.to_3x3().inverted().transposed();col=obj.data.materials[0].diffuse_color[:3]
 for tri in obj.data.loop_triangles:
  poly=obj.data.polygons[tri.polygon_index]
  for vi in tri.vertices:
   v=obj.data.vertices[vi];position=obj.matrix_world@v.co;normal=nm@(v.normal if poly.use_smooth else poly.normal);normal.normalize();data.extend((*position,*normal,*col))
(OUT/'model.bin').write_bytes(struct.pack('<'+'f'*len(data),*data))
(OUT/'model.json').write_text(json.dumps({'id':'everlost:default_wanderer','name':'První poutník','generated':False,'vertices':len(data)//9,'file':'model.bin'},ensure_ascii=False))
# Save an editable model, with a useful material preview viewport and no generated stills.
for screen in bpy.data.screens:
 for area in screen.areas:
  if area.type=='VIEW_3D':
   area.spaces.active.shading.type='MATERIAL';area.spaces.active.overlay.show_overlays=False
   area.spaces.active.region_3d.view_location=(0,0,1.42);area.spaces.active.region_3d.view_distance=5
   area.spaces.active.region_3d.view_rotation=(Vector((0,0,1.42))-Vector((3,-6,2.7))).to_track_quat('-Z','Y');area.spaces.active.region_3d.view_perspective='PERSP'
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'DefaultWanderer.blend'))
print('DEFAULT_SKIN_COMPLETE '+str(len(data)//9),flush=True)
