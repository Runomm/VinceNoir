import bpy, sys, os
out = sys.argv[sys.argv.index('--') + 1]
for o in bpy.context.view_layer.objects: o.select_set(False)
col = bpy.data.collections['Vince_Slim_Walker']
for o in col.all_objects:
    if o.type in ('MESH', 'ARMATURE'): o.select_set(True)
bpy.context.view_layer.objects.active = bpy.data.objects['VSW_Rig']
bpy.ops.export_scene.fbx(filepath=os.path.join(out, 'Vince_Slim_Walker.fbx'), use_selection=True,
    object_types={'MESH', 'ARMATURE'}, use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
    mesh_smooth_type='FACE', apply_scale_options='FBX_SCALE_ALL', path_mode='AUTO')
print('FBX OK')
