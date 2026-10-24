import bpy,math,os,json
from mathutils import Vector,Quaternion,Matrix,Euler
from math import sin,cos,pi
OUT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.dirname(OUT)+'/DressedCharacter/DressedWanderer.blend',load_ui=False)
rig=bpy.data.objects['Character_Rig'];body=bpy.data.objects['Reference_Character'];oldverts=[tuple(v.co) for v in body.data.vertices]
rig.animation_data_clear()
for a in list(bpy.data.actions):bpy.data.actions.remove(a)
bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for name in ['Axe_Grip','Pickaxe_Grip']:
 b=rig.data.edit_bones.new(name);b.head=(0,0,0);b.tail=(0,0,.3);b.parent=rig.data.edit_bones['root']
bpy.ops.object.mode_set(mode='OBJECT');rig.animation_data_create();rig.data.pose_position='POSE'
for p in rig.pose.bones:p.rotation_mode='QUATERNION'
def material(n,c,metal=0):
 m=bpy.data.materials.new(n);m.diffuse_color=(*c,1);m.use_nodes=True;bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*c,1);bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=.55;return m
wood=material('Tool handle hardwood',(.18,.075,.025));steel=material('Forged tool steel',(.20,.24,.26),.7);wrap=material('Grip leather',(.065,.035,.02));cut=material('Sharpened edge',(.48,.55,.57),.8)
def bind(o,bone,m):
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 for v in o.data.vertices:v.co+=o.location
 o.location=(0,0,0);o.parent=rig;o.data.materials.append(m);g=o.vertex_groups.new(name=bone);g.add(list(range(len(o.data.vertices))),1,'REPLACE');a=o.modifiers.new('Tool grip','ARMATURE');a.object=rig
 for p in o.data.polygons:p.use_smooth=True
 o.select_set(False)
def cylinder(n,z,length,r,bone,m):
 bpy.ops.mesh.primitive_cylinder_add(vertices=24,radius=r,depth=length,location=(0,0,z));o=bpy.context.object;o.name=n;bind(o,bone,m)
def prism(n,outline,depth,bone,m):
 vs=[(x,y,z) for x in [-depth/2,depth/2] for y,z in outline];N=len(outline);fs=[tuple(range(N-1,-1,-1)),tuple(range(N,2*N))]+[(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)]
 me=bpy.data.meshes.new(n);me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new(n,me);bpy.context.collection.objects.link(o);bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 b=o.modifiers.new('Forged bevel','BEVEL');b.width=.025;b.segments=3;bpy.ops.object.modifier_apply(modifier=b.name);bind(o,bone,m)
bpy.ops.object.select_all(action='DESELECT')
cylinder('Axe handle',.44,1.18,.047,'Axe_Grip',wood);cylinder('Axe grip',.06,.28,.053,'Axe_Grip',wrap)
prism('Axe steel head',[(-.14,.86),(-.48,.78),(-.62,.89),(-.62,1.15),(-.43,1.22),(-.12,1.11),(.12,1.10),(.12,.88)],.15,'Axe_Grip',steel)
prism('Axe polished cutting edge',[(-.57,.84),(-.65,.89),(-.65,1.16),(-.55,1.19)],.16,'Axe_Grip',cut)
cylinder('Pickaxe handle',.80,1.88,.044,'Pickaxe_Grip',wood);cylinder('Pickaxe grip',.07,.30,.052,'Pickaxe_Grip',wrap)
prism('Pickaxe curved head',[(-.77,1.87),(-.57,1.79),(-.22,1.72),(.18,1.72),(.52,1.79),(.77,1.87),(.45,1.65),(.12,1.56),(-.15,1.56),(-.48,1.65)],.12,'Pickaxe_Grip',steel)
# Bone orientations are solved in armature space; no constraints or scripts are needed in Unity.
I=Quaternion();scene=bpy.context.scene;scene.render.fps=30
rootrest=rig.data.bones['root'].matrix_local.to_quaternion()
def update():bpy.context.view_layer.update()
def delta(name):
 p=rig.pose.bones[name];return p.matrix.to_quaternion()@p.bone.matrix_local.to_quaternion().inverted()
def global_rotation(name,q):
 p=rig.pose.bones[name];rest=p.bone.matrix_local.to_quaternion();pd=delta(p.parent.name) if p.parent else I
 p.rotation_quaternion=rest.inverted()@pd.inverted()@q@rest;update()
def aim(name,target):
 p=rig.pose.bones[name];d=(Vector(target)-p.head).normalized();current=(p.tail-p.head).normalized();q=current.rotation_difference(d);global_rotation(name,q@delta(name))
def ik(a,b,target,pole):
 p=rig.pose.bones[a];start=p.head.copy();target=Vector(target);v=target-start;dist=v.length;L=p.bone.length;M=rig.pose.bones[b].bone.length;d=min(max(dist,abs(L-M)+.002),L+M-.008);axis=v.normalized();pole=Vector(pole)-start;perp=(pole-axis*pole.dot(axis)).normalized();along=(L*L-M*M+d*d)/(2*d);h=math.sqrt(max(.0001,L*L-along*along));joint=start+axis*along+perp*h;aim(a,joint);aim(b,start+axis*d)
def ease(x):x=max(0,min(1,x));return x*x*(3-2*x)
def curve(p,keys):
 for i in range(len(keys)-1):
  t,a=keys[i];u,b=keys[i+1]
  if p<=u:return a+(b-a)*ease((p-t)/(u-t))
 return keys[-1][1]
def mix(a,b,t):return Vector(a).lerp(Vector(b),t)
def tool_pose(name,origin,direction,active):
 p=rig.pose.bones[name];q=Vector((0,0,1)).rotation_difference(Vector(direction).normalized());desired=Matrix.LocRotScale(Vector(origin),q@p.bone.matrix_local.to_quaternion(),Vector((1,1,1)))
 p.matrix_basis=p.bone.convert_local_to_pose(desired,p.bone.matrix_local,parent_matrix=p.parent.matrix,parent_matrix_local=p.parent.bone.matrix_local,invert=True);p.scale=(1,1,1) if active else (0,0,0);update()
clips=[('Walk',36),('Run',24),('Chop',45),('Mine',54),('Swim',48)]
metrics={}
for name,length in clips:
 action=bpy.data.actions.new(name);action.use_fake_user=True;rig.animation_data.action=action
 for tr in rig.animation_data.nla_tracks:tr.mute=True
 maxima=0
 for f in range(length+1):
  scene.frame_set(f+1);p=f/length;theta=p*2*pi
  for b in rig.pose.bones:b.location=(0,0,0);b.rotation_quaternion=I;b.scale=(1,1,1)
  swim=name=='Swim';work=name in ['Chop','Mine'];R=Euler((pi/2,0,0)).to_quaternion() if swim else I.copy()
  rootmove=Vector((0,0,0));bend=0;twist=0;hit=0;u=Vector((0,0,1));wrist=Vector((0,-.6,3.0))
  if name=='Walk':rootmove.z=-.075+.025*cos(2*theta);bend=.045
  elif name=='Run':rootmove.z=-.14+.075*cos(2*theta);bend=.14
  elif name=='Chop':
   # Horizontal felling swing: wind up at the right side, strike forward,
   # then follow through to the left. The axe stays below the face.
   wind=curve(p,[(0,0),(.27,1),(.34,1),(.48,0),(1,0)])
   hit=curve(p,[(0,0),(.34,0),(.50,1),(.60,1),(1,0)])
   follow=curve(p,[(0,0),(.50,0),(.64,1),(.78,1),(1,0)])
   wrist=mix((-.08,-.66,3.00),(-.22,-.64,3.10),wind).lerp(Vector((.12,-.84,2.98)),hit).lerp(Vector((.36,-.67,2.94)),follow)
   u=mix((-.35,-.90,.18),(-.72,.67,.16),wind).lerp(Vector((.18,-.98,.05)),hit).lerp(Vector((.87,-.49,-.03)),follow).normalized()
   bend=.055+.07*hit;twist=-.32*wind+.30*follow;rootmove.z=-.065-.025*hit
  elif name=='Mine':
   lift=curve(p,[(0,0),(.30,1),(.39,1),(.55,0),(.70,0),(1,0)])
   hit=curve(p,[(0,0),(.40,0),(.56,1),(.66,1),(1,0)])
   wrist=mix((-.08,-.75,2.80),(-.12,-.88,3.60),lift).lerp(Vector((0,-1.05,2.28)),hit)
   u=mix((0,-.60,.80),(-.10,-.68,.727),lift).lerp(Vector((0,-.12,-.993)),hit).normalized();bend=.10+.82*hit;rootmove.z=-.09-.18*hit
  elif swim:
   # Rotate about the pelvis so the character floats horizontally.
   pivot=rig.data.bones['hips'].head_local;anchor=rig.data.bones['root'].head_local;rootmove=Vector((0,0,1.75+.035*cos(2*theta)))-(R@(pivot-anchor)+anchor)
  rig.pose.bones['root'].rotation_quaternion=rootrest.inverted()@R@rootrest;rig.pose.bones['root'].location=rootrest.inverted()@rootmove;update()
  global_rotation('hips',R@Euler((0,.035*sin(theta) if not work else 0,.045*sin(theta) if not work else twist*.25)).to_quaternion())
  global_rotation('spine',R@Euler((bend*.55,0,twist*.50)).to_quaternion())
  global_rotation('chest',R@Euler((bend,0,twist)).to_quaternion())
  global_rotation('neck',R@Euler((-bend*.10,0,twist*.25)).to_quaternion());global_rotation('head',R@Euler((-bend*.15,0,twist*.20)).to_quaternion())
  anchor=rig.data.bones['root'].head_local
  def world(v):return R@(Vector(v)-anchor)+anchor+rootmove
  for side,s in [('L',1),('R',-1)]:
   ph=theta+(0 if s==1 else pi)
   if name in ['Walk','Run']:
    run=name=='Run';stride=.57 if run else .35;lift=(.30 if run else .17)*max(0,sin(ph));ankle=Vector((s*.65,stride*cos(ph),.69+lift))
    # Feet use a common floor while the pelvis rises and falls.
    ankle.z-=rootmove.z;target=world(ankle)
   elif swim:target=world((s*.57,.10*sin(ph),.78+.17*cos(ph)))
   else:target=world((s*.67,s*.10,.69-rootmove.z))
   ik('thigh.'+side,'shin.'+side,target,world((s*.66,-1.2,1.45)))
   footangle=(-.12*max(0,sin(ph)) if name=='Walk' else -.22*max(0,sin(ph)) if name=='Run' else .13*sin(ph) if swim else 0)
   global_rotation('foot.'+side,R@Euler((footangle,0,0)).to_quaternion())
   if work:
    target=wrist+(u*.28 if side=='L' else Vector((0,0,0)));pole=(s*1.35,-.6,3.05)
   elif swim:
    # Alternating crawl: reach, pull underwater, recover with a bent elbow.
    a=ph;target=(s*(1.15+.12*max(0,sin(a))),-.15-.60*sin(a),3.30+.95*cos(a));pole=(s*1.6,-.3,3.2+.65*max(0,sin(a)))
   else:
    if name=='Run':
     # Elbows recover behind and outside the ribs; fists point forward
     # rather than inheriting the inward angle of the forearm.
     target=(s*1.03,-.28-.36*cos(ph),2.88+.055*sin(ph));pole=(s*1.78,.34,3.03)
    else:
     target=(s*.90,-.30*cos(ph),2.41+.035*max(0,sin(ph)));pole=(s*1.45,-.24,3.02)
   ik('upper_arm.'+side,'forearm.'+side,world(target),world(pole))
   # Keep palms in a consistent grip frame during work.
   if work:
    handdir=u;aim('hand.'+side,rig.pose.bones['hand.'+side].head+R@handdir*.3)
   elif swim:aim('hand.'+side,rig.pose.bones['hand.'+side].head+R@Vector((0,-.18,-.30)))
   elif name=='Run':aim('hand.'+side,rig.pose.bones['hand.'+side].head+Vector((s*.025,-.32,-.075)))
   else:aim('hand.'+side,rig.pose.bones['hand.'+side].head+Vector((s*.02,-.035,-.30)))
  origin=rig.pose.bones['hand.R'].head.copy()
  tool_pose('Axe_Grip',origin,R@u,name=='Chop');tool_pose('Pickaxe_Grip',origin,R@u,name=='Mine')
  for b in rig.pose.bones:
   b.keyframe_insert(data_path='location',frame=f+1,group=b.name);b.keyframe_insert(data_path='rotation_quaternion',frame=f+1,group=b.name);b.keyframe_insert(data_path='scale',frame=f+1,group=b.name)
 # Quaternion samples are baked, so linear interpolation avoids Bezier overshoot.
 for layer in action.layers:
  for strip in layer.strips:
   for bag in strip.channelbags:
    for fc in bag.fcurves:
     for k in fc.keyframe_points:k.interpolation='LINEAR'
 rig.animation_data.action=None
 track=rig.animation_data.nla_tracks.new();track.name=name;st=track.strips.new(name,1,action);st.extrapolation='NOTHING';track.mute=True
 metrics[name]={'frames':length+1,'seconds':length/30,'seamless_loop':True}
 print('CREATED',name,flush=True)
for t in rig.animation_data.nla_tracks:t.mute=t.name!='Walk'
scene.frame_start=1;scene.frame_end=37;scene.frame_set(1)
assert oldverts==[tuple(v.co) for v in body.data.vertices]
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/AnimatedWanderer.blend')
bpy.ops.object.select_all(action='DESELECT')
for o in scene.objects:
 if o==rig or o.parent==rig:o.select_set(True)
bpy.context.view_layer.objects.active=rig
for t in rig.animation_data.nla_tracks:t.mute=False
bpy.ops.export_scene.gltf(filepath=OUT+'/AnimatedWanderer.glb',use_selection=True,export_format='GLB',export_animations=True,export_animation_mode='NLA_TRACKS',export_skins=True)
# FBX for direct Unity import; each NLA strip becomes a separate animation take.
bpy.ops.export_scene.fbx(filepath=OUT+'/AnimatedWanderer.fbx',use_selection=True,add_leaf_bones=False,bake_anim=True,bake_anim_use_nla_strips=True,bake_anim_use_all_actions=False,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',object_types={'MESH','ARMATURE'},path_mode='AUTO')
json.dump({'clips':metrics,'original_body_unchanged':True,'old_actions_removed':True,'preview_tools_included':True},open(OUT+'/validation.json','w'),indent=2)
