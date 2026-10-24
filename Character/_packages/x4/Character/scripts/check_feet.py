"""Číselná kontrola lokomoce: pozice chodidel ve světě při pohybu vpřed (klouzání, směr)."""
import sys, bpy, json
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import anim_lib as A
args=sys.argv[sys.argv.index('--')+1:]
bpy.ops.wm.open_mainfile(filepath=args[0]); ob=bpy.data.objects['Traveler_Rig']; act=bpy.data.actions[args[1]]
ob.animation_data.action=act; v=act['speed_mps']; T=int(act.frame_end)
rows=[]; sc=bpy.context.scene
import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
data={s:{'yl':[],'yw':[],'z':[],'zt':[]} for s in ('.L','.R')}
for f in range(T+1):
    sc.frame_set(f)
    for s in ('.L','.R'):
        pb=ob.pose.bones['foot'+s]; toe=ob.pose.bones['toe'+s]
        a=pb.head; yl=a.y
        data[s]['yl'].append(yl); data[s]['yw'].append(yl - v*f/A.FPS); data[s]['z'].append(a.z); data[s]['zt'].append(toe.tail.z)
fig,ax=plt.subplots(1,2,figsize=(11,3.6))
for s,c in (('.L','tab:blue'),('.R','tab:orange')):
    ax[0].plot(data[s]['yl'],c=c,label='kotník'+s+' lokálně (in-place)'); ax[0].plot(data[s]['yw'],'--',c=c,label='kotník'+s+' ve světě')
    ax[1].plot(data[s]['z'],c=c,label='výška kotníku'+s); ax[1].plot(data[s]['zt'],':',c=c,label='špička'+s)
ax[0].set_title(f'{args[1]}: Y (dopředu = −Y), v={v:.2f} m/s'); ax[0].set_xlabel('snímek'); ax[0].legend(fontsize=7)
ax[1].set_title('výšky'); ax[1].legend(fontsize=7); plt.tight_layout(); plt.savefig(args[2],dpi=90)
# klouzání: v oporové fázi (toe tail z < 0.02 a kotník nízko) změna světové Y mezi snímky
slip=[]
for s in ('.L','.R'):
    for f in range(T):
        if data[s]['zt'][f]<0.012 and data[s]['zt'][f+1]<0.012:
            slip.append(abs(data[s]['zt'][f]-data[s]['zt'][f+1]))
print('local Y direction during stance check: right foot y frame0->5', round(data['.R']['yl'][0],3), round(data['.R']['yl'][5],3))
print('min toe z', round(min(min(data[s]['zt']) for s in data),4))
