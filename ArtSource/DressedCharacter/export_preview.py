import bpy,os,json,array
OUT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=OUT+'/DressedWanderer.blend',load_ui=False)
rig=bpy.data.objects['Character_Rig'];objs=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.parent==rig]
for o in objs:
 if len(o.data.polygons)>2000:
  # Browser-only reduction; saved Blender and GLB keep their full geometry.
  m=o.modifiers.new('Browser simplification','DECIMATE');m.ratio=.045 if o.name=='Reference_Character' else .10
rig.data.pose_position='REST'
bpy.ops.object.select_all(action='DESELECT')
for o in objs:
 bpy.context.view_layer.objects.active=o
 m=o.modifiers.get('Browser simplification')
 if m:
  bpy.ops.object.modifier_move_to_index(modifier=m.name,index=0)
  bpy.ops.object.modifier_apply(modifier=m.name)
rig.data.pose_position='POSE'
tracks=rig.animation_data.nla_tracks;clips=[]
for t in tracks:
 for q in tracks:q.mute=q!=t
 strip=t.strips[0];start,end=strip.frame_start,strip.frame_end;buf=array.array('f');nv=None
 for k in range(16):
  frame=start+(end-start)*k/16;bpy.context.scene.frame_set(int(frame),subframe=frame-int(frame));dg=bpy.context.evaluated_depsgraph_get();framebuf=array.array('f')
  for src in objs:
   ev=src.evaluated_get(dg);me=ev.to_mesh();me.calc_loop_triangles();mw=ev.matrix_world;nm=mw.to_3x3().inverted().transposed()
   for tri in me.loop_triangles:
    m=me.materials[tri.material_index];c=[max(0,v)**(1/2.2) for v in m.diffuse_color[:3]]
    for vi in tri.vertices:
     p=mw@me.vertices[vi].co;n=(nm@me.vertices[vi].normal).normalized();framebuf.extend((*p,*n,*c))
   ev.to_mesh_clear()
  if nv is None:nv=len(framebuf)//9
  assert len(framebuf)==nv*9,(t.name,k,'topology changed')
  buf.extend(framebuf)
 with open(OUT+'/'+t.name+'.bin','wb') as f:buf.tofile(f)
 clips.append({'name':t.name,'file':t.name+'.bin','vertices':nv,'frames':16,'duration':(end-start)/bpy.context.scene.render.fps})
 print('PREVIEW',t.name,nv,len(buf)*4,flush=True)
json.dump({'clips':clips,'source_actions_unchanged':True},open(OUT+'/animations.json','w'),indent=2)
