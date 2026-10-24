import bpy,os,json,math
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__));bpy.ops.wm.open_mainfile(filepath=OUT+'/AnimatedWanderer.blend',load_ui=False)
s=bpy.context.scene;r=bpy.data.objects['Character_Rig'];s.render.engine='CYCLES';s.cycles.samples=10;s.render.resolution_x=520;s.render.resolution_y=600;s.render.resolution_percentage=100
cam=s.camera;cam.data.type='ORTHO';cam.data.ortho_scale=8.5
metrics={}
for name,phase in [('Walk',.25),('Run',.25),('Chop',.32),('Chop',.52),('Mine',.33),('Mine',.57),('Swim',.0),('Swim',.5)]:
 track=r.animation_data.nla_tracks[name]
 for t in r.animation_data.nla_tracks:t.mute=t!=track
 st=track.strips[0];f=1+(st.frame_end-1)*phase;s.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
 target=Vector((0,0,3.1)) if name!='Swim' else Vector((0,-.1,1.8));cam.location=target+Vector((9,-17,6));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
 s.render.filepath=OUT+'/check_'+name+'_'+str(phase)+'.png';bpy.ops.render.render(write_still=True)
 # palm-to-handle and feet positions for numeric inspection
 metrics[name+str(phase)]={'right_wrist':list(r.pose.bones['hand.R'].head),'left_wrist':list(r.pose.bones['hand.L'].head),'ankles':[list(r.pose.bones['foot.'+q].head) for q in ['L','R']]}
json.dump(metrics,open(OUT+'/pose_checks.json','w'),indent=2)
