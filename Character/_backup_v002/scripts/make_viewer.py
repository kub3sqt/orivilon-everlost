"""Vloží GLB (base64) do HTML náhledu. python3 make_viewer.py template.html model.glb out.html"""
import sys, base64
t, g, o = sys.argv[1:4]
s = open(t, encoding='utf-8').read().replace('__GLB_BASE64__', base64.b64encode(open(g, 'rb').read()).decode())
open(o, 'w', encoding='utf-8').write(s)
print(o, len(s))
