---
name: character-production
description: Model, rig, animate and dress a stylized game character in Blender from a visual reference, with deformation and motion review before export. Use for character asset production, not Unity gameplay or general image generation.
---

# Character production

Read the active character brief before working. Work on actual editable 3D assets. Describe yourself as using an artist workflow; do not claim professional quality merely because a script completed.

## Shape and deformation

Read reference images visually before modeling. Compare front, profile, back and three-quarter silhouettes with orthographic views at the same scale. Resolve head/body ratio, leg length, mitten shape and foot size before adding detail. Smooth shading does not fix a faceted silhouette or poor topology.

Use intentional edge flow and sufficient geometry at shoulders, elbows, hips and knees. Build a coherent body surface, not an intersection of unconnected primitive limbs. Inspect bent joints before animation polish. Keep a non-destructive source and a separate export mesh when useful.

For the rig, provide reliable IK/FK, foot roll and stable elbow/knee poles, without overbuilding controls that will not be used. Inspect weights on extreme poses. Correct volume collapse and deformation at the mesh/weight level instead of hiding every failure beneath clothing. Bake deformation to ordinary bone transforms for interchange; Blender-only constraints are not a Unity runtime rig.

## Animation decisions

Block the meaningful poses first, then inspect the full motion at playback speed and slow motion from front, side and back. Plan contact, passing, down/up and recovery phases. Use asymmetry deliberately rather than adding arbitrary sine waves to every joint. Add secondary motion only after mechanics and contacts read clearly.

For in-place locomotion, choose and document the forward axis. During the planted phase the foot travels backward relative to the torso; during recovery it moves forward. Verify this visibly using a ground reference. Check heel/toe contact, pelvic weight shift, stride length and loop seam. Run arms should swing in the sagittal plane, with relaxed shoulders and hands clear of the ribs; prevent inward-pointing fists by controlling palm orientation.

For harvesting, use real tool dimensions and a target proxy. Establish body stance, two-hand grip, windup, impact and follow-through together. A tree-felling axe swing is lateral into the side of a trunk. A mining pick descends to a ground target. Test the entire swept tool path against the head, hat, arms and body, including interpolation between key poses. Exact grip points should stay on the handle; do not independently animate two hands and a tool and assume they agree.

Swimming should read as horizontal buoyant movement, with clear reach/catch/pull/recovery and coordinated kicks, without head/arm collisions. Keep the large stylized head and future hat in mind even before dressing.

## Dressing and delivery

Fit separate clothing to the approved body and share its deformation skeleton. Inspect cloth clearance during every action. Do not solve a naked rig failure by obscuring it with a cape. Distinguish useful cloth folds from noise. Match the brief's material palette and silhouette; avoid oversized boots and pouches.

Do not judge motion from a handful of attractive stills. Review full cycles with actual tools and target proxies. Automated checks supplement visual review; simplified collision geometry or sparse samples have limits and must be reported. Keep the original detailed asset as the source of truth. A reduced browser preview must not silently reverse time, mirror axes or change the motion.

Export a clean character without studio lights, floor or target proxies. Verify baked takes, transforms, scale, normals and material assignments by reimporting into a clean scene. State what was actually tested. Keep output local to the assigned folder and defer game integration when the brief asks only for a preview.
