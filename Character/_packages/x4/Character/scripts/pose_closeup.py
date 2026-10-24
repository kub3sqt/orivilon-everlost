import sys, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
exec(open('/home/claude/char/scripts/pose_test.py').read().split("RU.setup_workbench")[0])
RU.setup_workbench(res=(420,420))
shots=[('squat',(0.12,-0.1,0.33),(1,-1,0.1),0.5),('squat',(0.0,0.0,0.45),(0,-1,0.2),0.6),('elbow',(0.25,0,0.85),(1,-0.6,0),0.5),('elbow',(0.25,0,0.85),(0.3,1,0),0.5),
       ('arms_up',(0.2,0,0.95),(1,-0.5,0),0.6),('arms_up',(0.2,0,0.95),(0.5,1,0.2),0.6),('arms_fwd',(0.2,0,0.95),(0.8,-0.6,0.5),0.6),('bend',(0,0.1,0.65),(1,0,0.2),0.7)]
out=[]
for i,(name,t,d,sc) in enumerate(shots):
    PL.clear(ob); dict(poses)[name](); PL.update()
    RU.camera(target=Vector(t),d=Vector(d).normalized(),ortho_scale=sc); p=f'/tmp/_pc{i}.png'; RU.render(p); out.append(p)
RU.compose(out, sys.argv[-1], cols=4, labels=[s[0] for s in shots])
