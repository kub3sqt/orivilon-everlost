import bpy,os,json,math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
OUT=os.path.dirname(os.path.abspath(__file__));bpy.ops.wm.open_mainfile(filepath=OUT+'/AnimatedWanderer.blend',load_ui=False)
r=bpy.data.objects['Character_Rig'];scene=bpy.context.scene;body=bpy.data.objects['Reference_Character'];tracks=r.animation_data.nla_tracks
# Simplify only the collision-test copy in memory; saved model stays untouched.
r.data.pose_position='REST';bpy.context.view_layer.objects.active=body
m=body.modifiers.new('Collision test simplification','DECIMATE');m.ratio=.10;bpy.ops.object.modifier_move_to_index(modifier=m.name,index=1);bpy.ops.object.modifier_apply(modifier=m.name)
r.data.pose_position='POSE'
def pose(name,frame):
 for t in tracks:t.mute=t.name!=name
 scene.frame_set(int(frame),subframe=frame-int(frame));bpy.context.view_layer.update()
def collider(o,head=False):
 ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();idx=o.vertex_groups['head'].index if head else -1
 good={v.index for v in me.vertices if not head or any(g.group==idx and g.weight>.25 for g in v.groups)}
 faces=[tuple(p.vertices) for p in me.polygons if all(i in good for i in p.vertices)];verts=[ev.matrix_world@v.co for v in me.vertices]
 tree=BVHTree.FromPolygons(verts,faces,all_triangles=False) if faces else None;ev.to_mesh_clear();return tree,len(faces)
report={}
for name in ['Walk','Run']:
 pose(name,tracks[name].strips[0].frame_end*.55);y0=r.pose.bones['foot.L'].head.y
 pose(name,tracks[name].strips[0].frame_end*.85);y1=r.pose.bones['foot.L'].head.y
 assert y1>y0,(name,'planted foot moves forward instead of backward')
 report[name]={'stance_foot_motion_positive_y':y1-y0}
# Ensure fists and elbows remain outside the sides of the torso for the complete run.
mins={'wrist':100,'elbow':100,'fist':100}
for frame in range(1,26):
 pose('Run',frame)
 for side in ['L','R']:
  mins['wrist']=min(mins['wrist'],abs(r.pose.bones['hand.'+side].head.x))
  mins['elbow']=min(mins['elbow'],abs(r.pose.bones['forearm.'+side].head.x))
  mins['fist']=min(mins['fist'],abs(r.pose.bones['hand.'+side].tail.x))
assert mins['wrist']>.86 and mins['elbow']>.88 and mins['fist']>.86,mins
report['run_arm_clearance']=mins
hats=[o for o in scene.objects if o.type=='MESH' and ('hat' in o.name.lower() and o.parent==r)]
for name,toolprefix in [('Mine','Pickaxe'),('Chop','Axe')]:
 collisions=[];samples=0;faces=0;end=int(tracks[name].strips[0].frame_end)
 tools=[o for o in scene.objects if o.type=='MESH' and o.name.startswith(toolprefix)]
 for half in range(2*(end-1)+1):
  pose(name,1+half/2);trees=[]
  head,n=collider(body,True);faces=n;trees.append(head)
  for h in hats:
   tree,_=collider(h)
   if tree:trees.append(tree)
  for tool in tools:
   tree,_=collider(tool)
   if any(c.overlap(tree) for c in trees if c):collisions.append({'frame':1+half/2,'tool':tool.name})
  samples+=1
 report[name]={'sampled_poses':samples,'head_faces_tested':faces,'head_or_hat_intersections':collisions}
 print(name,report[name],flush=True)
 assert not collisions,(name,collisions)
# The axe now sweeps sideways at waist/chest height rather than overhead.
points=[]
for f in range(1,47):
 pose('Chop',f);b=r.pose.bones['Axe_Grip'];p=b.matrix@b.bone.matrix_local.inverted()@Vector((0,0,1.0));points.append(p)
report['axe_arc']={'horizontal_span':max(v.x for v in points)-min(v.x for v in points),'max_height':max(v.z for v in points),'min_height':min(v.z for v in points)}
assert report['axe_arc']['horizontal_span']>1.0 and report['axe_arc']['max_height']<3.8
json.dump(report,open(OUT+'/motion_regressions.json','w'),indent=2);print('ALL MOTION CHECKS PASS',flush=True)
