import bpy, sys, os, math
from mathutils import Vector
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]; SHOTS = argv[1].split(',')
SAMPLES = int(argv[2]) if len(argv) > 2 else 48
SCALE = int(argv[3]) if len(argv) > 3 else 100
scene = bpy.context.scene

scene.render.engine = 'BLENDER_EEVEE'
ee = scene.eevee
for k, v in (('volumetric_tile_size', '4'), ('volumetric_end', 25.0), ('volumetric_samples', 64), ('taa_render_samples', SAMPLES), ('use_raytracing', True), ('use_shadows', True),
             ('shadow_ray_count', 2), ('shadow_step_count', 8), ('use_volumetric_shadows', True)):
    try: setattr(ee, k, v)
    except Exception as e: print('eevee setting fail', k, e)
try:
    ee.ray_tracing_options.resolution_scale = '1'
    ee.fast_gi_method = 'GLOBAL_ILLUMINATION'
except Exception as e: print('rt opts', e)
try:
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
except Exception as e: print('cm', e)
scene.render.film_transparent = False
scene.view_settings.exposure = -1.0
scene.render.image_settings.file_format = 'PNG'
scene.render.resolution_percentage = SCALE

def set_cam(name, loc, target, lens=50, ortho=None, res=(1200, 1200), dof=None):
    cd = bpy.data.cameras.get(name) or bpy.data.cameras.new(name)
    co = bpy.data.objects.get(name)
    if co is None:
        co = bpy.data.objects.new(name, cd); scene.collection.objects.link(co)
    co.location = loc
    co.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    if ortho:
        cd.type = 'ORTHO'; cd.ortho_scale = ortho
    else:
        cd.type = 'PERSP'; cd.lens = lens
    cd.clip_end = 100
    cd.dof.use_dof = bool(dof)
    if dof: cd.dof.focus_distance = dof[0]; cd.dof.aperture_fstop = dof[1]
    scene.camera = co
    scene.render.resolution_x, scene.render.resolution_y = res

def alley(on):
    c = bpy.data.collections.get('Env_Alley'); c.hide_render = not on

def render(name):
    scene.render.filepath = os.path.join(OUT, name + '.png')
    bpy.ops.render.render(write_still=True)
    return scene.render.filepath

from mathutils import Quaternion
RIG = bpy.data.objects.get('VSW_Rig')
def pose(spec):
    if not RIG: return
    for pb in RIG.pose.bones:
        pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = (1, 0, 0, 0)
    for name, rots in spec.items():
        pb = RIG.pose.bones[name]; Mr = pb.bone.matrix_local.to_3x3()
        q = Quaternion()
        for axis, deg in rots: q = Quaternion(Vector(axis), math.radians(deg)) @ q
        pb.rotation_quaternion = (Mr.inverted() @ q.to_matrix() @ Mr).to_quaternion()
    bpy.context.view_layer.update()
HERO_POSE = {
    'Shoulder.L': [((0, 1, 0), 8)], 'Shoulder.R': [((0, 1, 0), -8)],
    'UpperArm.L': [((0, 1, 0), 55), ((0, 0, 1), -12)], 'UpperArm.R': [((0, 1, 0), -55), ((0, 0, 1), 12)],
    'ForeArm.L': [((0, 0, 1), -20)], 'ForeArm.R': [((0, 0, 1), 20)],
    'Hand.L': [((1, 0, 0), -8)], 'Hand.R': [((1, 0, 0), -8)],
    'Head': [((0, 0, 1), 14), ((1, 0, 0), 4)], 'Neck': [((0, 1, 0), -3)],
    'Chest': [((0, 0, 1), -5)], 'Spine': [((0, 1, 0), 2)],
    'Thigh.L': [((0, 1, 0), 3)], 'Thigh.R': [((0, 1, 0), -2), ((1, 0, 0), -4)], 'Shin.R': [((1, 0, 0), 8)],
}
for side, s in (('.L', 1), ('.R', -1)):
    for fn, ang in (('Index', 12), ('Middle', 16), ('Ring', 20), ('Pinky', 24)):
        for k in (1, 2, 3):
            HERO_POSE['%s%d%s' % (fn, k, side)] = [((0, -1, 0) if s > 0 else (0, 1, 0), 0)]
made = {}
for s in SHOTS:
    if s == 'front':
        alley(False); set_cam('Cam_Front', (0, -12, 0.95), (0, 0, 0.95), ortho=2.15, res=(1000, 1100))
    elif s == 'side':
        alley(False); set_cam('Cam_Side', (-12, 0, 0.95), (0, 0, 0.95), ortho=2.15, res=(1000, 1100))
    elif s == 'back':
        alley(False); set_cam('Cam_Back', (0, 12, 0.95), (0, 0, 0.95), ortho=2.15, res=(1000, 1100))
    elif s == 'hero':
        alley(True); set_cam('Cam_Hero', (1.05, -2.75, 0.62), (0.12, 0.0, 1.02), lens=28, res=(1920, 1080), dof=(2.95, 4.0))
    elif s == 'heroface':
        alley(True); set_cam('Cam_HeroFace', (0.75, -1.55, 1.62), (0.02, 0.0, 1.45), lens=50, res=(1080, 1350), dof=(1.75, 2.8))
    elif s == 'face':
        alley(True); set_cam('Cam_Face', (0.33, -0.82, 1.74), (0.0, 0.0, 1.715), lens=85, res=(1080, 1080), dof=(0.80, 4.0))
    elif s == 'faceside':
        alley(True); set_cam('Cam_FaceSide', (-0.85, -0.25, 1.73), (0.0, 0.0, 1.71), lens=85, res=(1080, 1080))
    elif s == 'hand':
        alley(True); set_cam('Cam_Hand', (0.75, -0.55, 1.55), (0.80, 0.0, 1.38), lens=60, res=(1080, 1080))
    elif s == 'feet':
        alley(True); set_cam('Cam_Feet', (0.5, -1.1, 0.35), (0.0, 0.0, 0.12), lens=50, res=(1080, 1080))
    elif s == 'torso':
        alley(True); set_cam('Cam_Torso', (0.25, -1.5, 1.25), (0.0, 0.0, 1.2), lens=50, res=(1080, 1080))
    pose(HERO_POSE if s in ('hero', 'heroface') else {})
    made[s] = render(s)
    print('RENDERED', made[s])

if all(k in made for k in ('front', 'side', 'back')):
    arrs = []
    for k in ('front', 'side', 'back'):
        im = bpy.data.images.load(made[k]); w, h = im.size
        a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4); arrs.append(a)
    sheet = np.concatenate(arrs, axis=1); h, w = sheet.shape[:2]
    out = bpy.data.images.new('turnaround', w, h)
    out.pixels = sheet.ravel()
    out.filepath_raw = os.path.join(OUT, 'turnaround.png'); out.file_format = 'PNG'; out.save()
    print('SHEET', out.filepath_raw)
