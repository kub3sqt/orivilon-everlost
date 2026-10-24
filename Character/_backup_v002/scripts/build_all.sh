#!/bin/bash
# Kompletní reprodukce: tělo -> rig -> animace -> oblečení (Blender 5.1 bpy / Blender 5.x)
set -e
cd "$(dirname "$0")/.."
PY=${PY:-python3}
mkdir -p work 01_body 02_rig_anim 03_dressed
$PY scripts/build_body.py -- 01_body/Traveler_Body_White_v002.blend
$PY scripts/make_rig_file.py -- 01_body/Traveler_Body_White_v002.blend work/rig_base.blend
$PY scripts/anim_locomotion.py -- work/rig_base.blend work/anim_loco.blend
$PY scripts/anim_tools.py -- work/anim_loco.blend work/anim_tools.blend
$PY scripts/anim_swim.py -- work/anim_tools.blend work/anim_swim.blend
$PY scripts/anim_idle.py -- work/anim_swim.blend work/anim_all.blend
cp work/anim_all.blend 02_rig_anim/Traveler_White_Rig_Anim_v002.blend
$PY scripts/build_clothes.py -- work/anim_all.blend work/dressed.blend
cp work/dressed.blend 03_dressed/Traveler_Dressed_v002.blend
