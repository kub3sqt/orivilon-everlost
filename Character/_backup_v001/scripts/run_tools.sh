#!/bin/bash
# rebuild tool actions + check + key grid
cd /home/claude/char
python3 scripts/anim_tools.py -- work/anim_loco.blend work/anim_tools.blend 2>&1 | grep -E "Error|Trace|rror:"
for a in $@; do
python3 scripts/check_action.py -- work/anim_tools.blend $a sub=1 review/$a.json >/dev/null 2>&1
python3 -c "
import json
d=json.load(open('review/$a.json')); print('$a', {k:v for k,v in d['summary'].items() if 'err_contact' not in k and 'target' not in k})
print(' '.join(f\"{int(r['t'])}:{r['armL_in_torso_mm']:.0f}/{r['armR_in_torso_mm']:.0f}/{r['tool_in_torso_mm']:.0f}/{r['tool_hat_clear_mm']:.0f}\" for r in d['frames']))"
done
