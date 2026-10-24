import sys, numpy as np, bpy
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import importlib, body_sdf; importlib.reload(body_sdf)
from skimage.measure import marching_cubes
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import render_util as RU

res = 0.006
xs = np.arange(-0.62, 0.62, res); ys = np.arange(-0.3, 0.3, res); zs = np.arange(-0.02, 1.56, res)
X, Y, Z = np.meshgrid(xs, ys, zs, indexing='ij')
F = body_sdf.body_sdf(np.stack([X, Y, Z], -1))
v, f, n, _ = marching_cubes(F, 0.0, spacing=(res, res, res))
v += np.array([xs[0], ys[0], zs[0]])
RU.reset()
me = bpy.data.meshes.new('sdf'); me.from_pydata(v.tolist(), [], f[:, ::-1].tolist()); me.update()
ob = bpy.data.objects.new('sdf', me); bpy.context.collection.objects.link(ob)
for p in me.polygons: p.use_smooth = True
print('height', v[:, 2].max(), 'width', v[:, 0].max() * 2)
RU.turn_sheet([ob], sys.argv[-1])
