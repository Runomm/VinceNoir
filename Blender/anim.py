# Adds hand-held pistol parts (frame / slide / magazines), gun bones, a mag pouch and procedural game animations
# to Vince_Slim_Walker.blend, then exports the animated FBX for Unity.
# usage: blender -b Vince_Slim_Walker.blend --python anim.py -- <out_dir> [preview] [mixamotest]
import bpy, bmesh, math, os, sys
from math import sin, cos, pi, radians, exp
from mathutils import Vector, Matrix, Quaternion

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]
PREVIEW = 'preview' in argv[1:]
MIXAMO_TEST = 'mixamotest' in argv[1:]
scene = bpy.context.scene
scene.render.fps = 30
rig = bpy.data.objects['VSW_Rig']
CHAR = bpy.data.collections['Vince_Slim_Walker']

def V(*a): return Vector(a)
def clamp(x, a, b): return max(a, min(b, x))
def ss(e0, e1, x):
    t = clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)
def gp(p, c, w):  # periodic gaussian on [0,1)
    d = (p - c + 0.5) % 1.0 - 0.5
    return exp(-(d / w) ** 2)
def upd(): bpy.context.view_layer.update()

DR = radians(8)
def arm_frame(sgn):
    return V(sgn * 0.19, 0.006, 1.455), V(sgn * cos(DR), 0, -sin(DR)), V(sgn * sin(DR), 0, cos(DR))

# unique names for Unity Humanoid (mesh objects must not share names with bones)
for old, new in (('Head', 'Head_Mesh'), ('Neck', 'Neck_Mesh')):
    o = bpy.data.objects.get(old)
    if o and o.type == 'MESH': o.name = new

# ------------------------------------------------------------------ hand-held pistol geometry
S, D, U = arm_frame(-1)
Lh = V(0, -1, 0)
Uv = D.cross(Lh)
W = S + D * 0.552
def HP(f, l, u): return W + D * f + Lh * l + U * u
Fw = V(0, 0.17, -0.985).normalized(); Uw = V(0, 0.985, 0.17).normalized(); Sw = Fw.cross(Uw)
yaw = Matrix.Rotation(radians(-12), 3, 'Z'); Fw, Uw, Sw = yaw @ Fw, yaw @ Uw, yaw @ Sw
G0 = V(-0.214, 0.010, 1.085)
gripC = HP(0.068, -0.004, -0.032)
Gh = gripC + D * 0.011 + Lh * 0.058          # gun origin (slide bottom, rear) in the right hand
def GP(f, u, s=0.0): return Gh + D * f + Lh * u + Uv * s
A_h = Matrix((Fw, Uw, Sw)).transposed()
A_n = Matrix((D, Lh, Uv)).transposed()
M = Matrix.Translation(Gh) @ (A_n @ A_h.transposed()).to_4x4() @ Matrix.Translation(-G0)
G18 = radians(18)
g_vec = (D * -sin(G18) + Lh * -cos(G18)).normalized()   # down the grip
gp_vec = (D * cos(G18) + Lh * -sin(G18)).normalized()   # grip front
grip_top = GP(0.004, -0.012)

def armature_mod(o):
    o.modifiers.clear()
    m = o.modifiers.new('Armature', 'ARMATURE'); m.object = rig; m.use_deform_preserve_volume = True
    o.parent = rig
    return m

def weight_all(o, group):
    o.vertex_groups.clear()
    vg = o.vertex_groups.new(name=group); vg.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')

def box_mesh(name, parts, mat):
    bm = bmesh.new()
    for c, ax, ay, az, hx, hy, hz in parts:
        r = bmesh.ops.create_cube(bm, size=2.0)
        for v in r['verts']:
            p = v.co.copy(); v.co = c + ax * (p.x * hx) + ay * (p.y * hy) + az * (p.z * hz)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); CHAR.objects.link(o); me.materials.append(mat)
    return o

def split_by_islands(obj, keep_pred, new_name):
    """moves islands whose centroid satisfies keep_pred into a new object"""
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    seen = set(); move = set()
    for v in bm.verts:
        if v.index in seen: continue
        stack = [v]; isl = []
        while stack:
            x = stack.pop()
            if x.index in seen: continue
            seen.add(x.index); isl.append(x.index)
            for e in x.link_edges: stack.append(e.other_vert(x))
        c = sum((bm.verts[i].co for i in isl), Vector()) / len(isl)
        if keep_pred(c): move.update(isl)
    bm.free()
    n = obj.copy(); n.data = obj.data.copy(); n.name = new_name; n.data.name = new_name; CHAR.objects.link(n)
    for o, delete_moved in ((obj, True), (n, False)):
        bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
        dele = [v for v in bm.verts if (v.index in move) == delete_moved]
        bmesh.ops.delete(bm, geom=dele, context='VERTS'); bm.to_mesh(o.data); bm.free(); o.data.update()
    return n

# ------------------------------------------------------------------ smoking props geometry (rest pose)
def obj_centroid(name):
    o = bpy.data.objects[name]
    return sum((o.matrix_world @ v.co for v in o.data.vertices), Vector()) / len(o.data.vertices)
CIG_F = obj_centroid('Cig_Filter'); CIG_E = obj_centroid('Cig_Ember')
CIG_D = (CIG_E - CIG_F).normalized()
MOUTH_P0 = CIG_F - CIG_D * 0.012                      # filter end of the mouth cigarette (right corner of the lips)
MOUTH_C = MOUTH_P0 + V(0.0225, -0.004, 0.0)           # centre of the lips
SL, DLh, UL = arm_frame(1); WL = SL + DLh * 0.552; LL = V(0, -1, 0)
def HPl(f, l, u): return WL + DLh * f + LL * l + UL * u
HANDCIG_0 = HPl(0.128, 0.018, -0.012)                 # filter end of the cigarette held between index and middle finger
PACK_C = HPl(0.055, 0.0, -0.028); PACK_TOP = PACK_C + LL * 0.0435
LIGHTER_C = HP(0.055, 0.005, -0.028); LIGHTER_TOP = LIGHTER_C + Lh * 0.018
def obj_frame(origin, x, y):
    x = x.normalized(); y = (y - x * y.dot(x)).normalized()
    return Matrix.Translation(origin) @ Matrix((x, y, x.cross(y))).transposed().to_4x4()
HANDCIG_REST = obj_frame(HANDCIG_0, UL, DLh)
PACK_REST = obj_frame(PACK_TOP, LL, DLh)
LIGHTER_REST = obj_frame(LIGHTER_TOP, Lh, D)
MOUTHCIG_FRAME = obj_frame(MOUTH_P0, CIG_D, V(0, 0, 1))
def simple_mat(name, col, metal=0.0, rough=0.5):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    try: m.use_nodes = True
    except Exception: pass
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*col, 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    return m

if 'Pistol_Hand' not in bpy.data.objects:
    for src, dst in (('Pistol', 'Pistol_Hand'), ('Pistol_Grips', 'Pistol_Hand_Grips')):
        o = bpy.data.objects[src]
        n = o.copy(); n.data = o.data.copy(); n.name = dst; n.data.name = dst
        CHAR.objects.link(n)
        n.data.transform(M); n.data.update()
        weight_all(n, 'Gun.R')
        o.name = src.replace('Pistol', 'Pistol_Holster')
    def is_slide(c):
        rel = c - Gh
        return rel.dot(Lh) > 0.004 and rel.dot(D) > -0.016
    slide = split_by_islands(bpy.data.objects['Pistol_Hand'], is_slide, 'Pistol_Hand_Slide')
    weight_all(slide, 'Slide.R')
    # mirrored pistol in the LEFT hand (bots that switch hands after the gun arm is hit; hidden on Vince)
    for src, dst in (('Pistol_Hand', 'BotPistolL'), ('Pistol_Hand_Grips', 'BotPistolL_Grips'), ('Pistol_Hand_Slide', 'BotPistolL_Slide')):
        o = bpy.data.objects[src]
        n = o.copy(); n.data = o.data.copy(); n.name = dst; n.data.name = dst; CHAR.objects.link(n)
        n.data.transform(Matrix.Scale(-1, 4, (1, 0, 0)))
        bm = bmesh.new(); bm.from_mesh(n.data); bmesh.ops.reverse_faces(bm, faces=bm.faces); bm.to_mesh(n.data); bm.free(); n.data.update()
        weight_all(n, 'Gun.L')
    gunmetal = bpy.data.materials['Gun_Blued']
    mag = box_mesh('Mag_InGun', [(grip_top + g_vec * 0.050, gp_vec, g_vec, Uv, 0.0105, 0.049, 0.0085),
                                 (grip_top + g_vec * 0.1015, gp_vec, g_vec, Uv, 0.0155, 0.0035, 0.0125)], gunmetal)
    weight_all(mag, 'Gun.R'); armature_mod(mag)
    b = mag.modifiers.new('Bevel', 'BEVEL'); b.width = 0.0012; b.segments = 2
    # (the spare magazine comes out of the left jacket pocket - no pouch on the belt)
    # cigarette held in the left hand (copy of the mouth cigarette), pack (left hand) and lighter (right hand)
    for src in ('Cig_Filter', 'Cig_Paper', 'Cig_Ash', 'Cig_Ember'):
        o = bpy.data.objects[src]
        n = o.copy(); n.data = o.data.copy(); n.name = 'Hand' + src; n.data.name = n.name; CHAR.objects.link(n)
        n.data.transform(HANDCIG_REST @ MOUTHCIG_FRAME.inverted()); n.data.update()
        weight_all(n, 'Hand.L'); armature_mod(n)
    red = simple_mat('Cig_Pack', (0.42, 0.02, 0.02), 0.0, 0.45); white = simple_mat('Cig_Pack_White', (0.85, 0.83, 0.78), 0.0, 0.5)
    pk = box_mesh('Cig_Pack', [(PACK_C, LL, DLh, UL, 0.0435, 0.027, 0.011)], red)
    pkt = box_mesh('Cig_Pack_Top', [(PACK_C + LL * 0.036, LL, DLh, UL, 0.0078, 0.0273, 0.0113)], white)
    lt = box_mesh('Lighter', [(LIGHTER_C, Lh, D, U, 0.018, 0.012, 0.006)], bpy.data.materials['Brass_Old'])
    for o, grp in ((pk, 'Hand.L'), (pkt, 'Hand.L'), (lt, 'Hand.R')):
        weight_all(o, grp); armature_mod(o)
        bv = o.modifiers.new('Bevel', 'BEVEL'); bv.width = 0.0015; bv.segments = 2

    bpy.context.view_layer.objects.active = rig
    for o in bpy.context.view_layer.objects: o.select_set(False)
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = rig.data.edit_bones
    g = eb.new('Gun.R'); g.head = Gh + Lh * 0.017; g.tail = g.head + D * 0.176; g.parent = eb['Hand.R']
    m = eb.new('Muzzle.R'); m.head = g.tail; m.tail = g.tail + D * 0.04; m.parent = g; m.use_connect = True
    gu = eb.new('GunUp.R'); gu.head = g.head + Lh * 0.05; gu.tail = gu.head + D * 0.02; gu.parent = g
    gl = eb.new('GripL.R'); gl.head = gripC + U * (-0.034) - D * 0.004; gl.tail = gl.head + D * 0.02; gl.parent = g
    sl = eb.new('Slide.R'); sl.head = GP(-0.02, 0.0165); sl.tail = sl.head + D * 0.05; sl.parent = g
    def mx(v): return V(-v.x, v.y, v.z)
    gL = eb.new('Gun.L'); gL.head = mx(g.head); gL.tail = mx(g.tail); gL.roll = -g.roll; gL.parent = eb['Hand.L']
    mL = eb.new('Muzzle.L'); mL.head = gL.tail; mL.tail = mx(m.tail); mL.parent = gL; mL.use_connect = True
    # smoking helper points: cigarette tips (smoke / ember light), mouth (exhaled smoke), lighter flame
    ct = eb.new('CigTipHand.L'); ct.head = HANDCIG_0 + UL * 0.072; ct.tail = ct.head + UL * 0.02; ct.parent = eb['Hand.L']
    cm = eb.new('CigTipMouth'); cm.head = CIG_E + CIG_D * 0.004; cm.tail = cm.head + CIG_D * 0.02; cm.parent = eb['Head']
    mo = eb.new('Mouth'); mo.head = MOUTH_C + V(0, -0.01, 0); mo.tail = mo.head + V(0, -0.03, 0); mo.parent = eb['Head']
    lf = eb.new('LighterFlame.R'); lf.head = LIGHTER_TOP + Lh * 0.012; lf.tail = lf.head + Lh * 0.02; lf.parent = eb['Hand.R']
    bpy.ops.object.mode_set(mode='OBJECT')

# ------------------------------------------------------------------ jacket flaps: the left front panel swings open (shoulder holster)
# and the back hem lifts (gun behind the back). Every torso bone gets a child "flap" bone and part of the jacket weights
# moves to it, so with the flaps closed the jacket deforms exactly as before. Unity drives the flap bones (VincePlayer).
JF_HINGE = (0.175, 0.0)               # vertical hinge line (x, y) at the left side seam
# back hem: gathered up to the waist (squashed toward the pivot height and pulled a little away from the back) so the
# shirt and the pistol show underneath; the centre goes highest, the sides follow in a curve. Unity scales / moves the
# JkB bones (VincePlayer.ApplyJacket) - the clips only carry the hands.
JB_PIV = V(0.0, 0.125, 1.20)
JF_DEG, JB_SQUASH, JB_BACK = 55.0, 0.22, 0.05
TORSO = ('Hips', 'Spine', 'Chest')
def mask_front(p): return ss(0.03, 0.06, p.x) * ss(-0.02, -0.09, p.y) * ss(0.93, 1.10, p.z) * ss(1.50, 1.40, p.z)
def mask_back(p): return ss(-0.02, 0.06, p.y) * ss(0.21, 0.11, abs(p.x)) * ss(1.24, 1.15, p.z)
if 'JkF_Spine' not in rig.data.bones:
    bpy.context.view_layer.objects.active = rig
    for o in bpy.context.view_layer.objects: o.select_set(False)
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = rig.data.edit_bones
    for par, z in (('Hips', 1.0), ('Spine', 1.16), ('Chest', 1.32)):
        b = eb.new('JkF_' + par); b.head = V(JF_HINGE[0], JF_HINGE[1], z); b.tail = b.head + V(0, 0, 0.08); b.parent = eb[par]
        b1 = eb.new('JkB_' + par); b1.head = JB_PIV.copy(); b1.tail = JB_PIV + V(0, 0, 0.08); b1.parent = eb[par]   # bone Y = up
    bpy.ops.object.mode_set(mode='OBJECT')
    for name in ('Jacket_Body', 'Jacket_Lapels', 'Jacket_PocketFlaps', 'Jacket_Buttons'):
        ob = bpy.data.objects[name]
        src = {par: ob.vertex_groups.get(par) for par in TORSO}
        idx = {g.index: par for par, g in src.items() if g}
        dst = {}
        for v in ob.data.vertices:
            p = ob.matrix_world @ v.co
            mf, mb = mask_front(p), mask_back(p)
            if mf + mb <= 1e-4: continue
            entries = [(idx[ge.group], ge.weight) for ge in v.groups if ge.group in idx]
            for par, w in entries:
                for tag, m in (('JkF_', mf), ('JkB_', mb)):
                    if m > 1e-4:
                        g = dst.get(tag + par) or ob.vertex_groups.new(name=tag + par); dst[tag + par] = g
                        g.add([v.index], w * m, 'REPLACE')
                src[par].add([v.index], w * (1 - mf - mb), 'REPLACE')
        print('JACKET FLAPS', name, sorted(dst))

for pb in rig.pose.bones:
    pb.rotation_mode = 'QUATERNION'
upd()

def jacket_pose(p, front=0.0, back=0.0):
    for par in TORSO:
        if front: p.add('JkF_' + par, [(ZA, JF_DEG * front)])
    return p   # the back hem is gathered by bone scale (set by Unity, and by the preview below)
def flap_front(pt, open_):
    piv = V(JF_HINGE[0], JF_HINGE[1], pt.z)
    return piv + Quaternion(V(0, 0, 1), radians(JF_DEG * open_)) @ (pt - piv)
def flap_back(pt, open_):
    """a point of the back panel (fully masked) once the hem is gathered up"""
    s = 1.0 - (1.0 - JB_SQUASH) * open_
    return V(pt.x, pt.y + JB_BACK * open_, JB_PIV.z + (pt.z - JB_PIV.z) * s)
def set_back_gather(o):
    """preview only: scale the JkB bones like Unity does"""
    s = 1.0 - (1.0 - JB_SQUASH) * o
    for par in TORSO:
        pb = rig.pose.bones.get('JkB_' + par)
        if pb: pb.scale = (1, s, 1); pb.location = MR[pb.name].inverted() @ V(0, JB_BACK * o, 0)

# ------------------------------------------------------------------ holster: realistic strong-side orientation
# muzzle down (slight FBI cant), slide facing forward, grip pointing back/up so the hand can draw it
def frame_mat(origin, f, u):
    return Matrix.Translation(origin) @ Matrix((f, u, f.cross(u))).transposed().to_4x4()
FwN = yaw @ V(0, 0.26, -0.966).normalized()
UwN = yaw @ V(0, -0.966, -0.26).normalized()
_c_old = G0 + Fw * 0.11 + Uw * 0.008
G0N = _c_old - FwN * 0.11 - UwN * 0.008
hol = bpy.data.objects['Holster']
if not hol.get('reoriented'):
    T_hol = frame_mat(G0N, FwN, UwN) @ frame_mat(G0, Fw, Uw).inverted()
    for n in ('Holster', 'Pistol_Holster', 'Pistol_Holster_Grips'):
        o = bpy.data.objects[n]; o.data.transform(T_hol); o.data.update()
    hol['reoriented'] = True
HOLSTER_GUN = (G0N + UwN * 0.017, FwN, UwN, V(-0.42, 0.55, 1.02))     # Gun.R (bore rear) pose of the holstered pistol (+ elbow pole)

# ---- two more ways to carry: shoulder holster under the jacket (left armpit) and tucked behind the back
# shoulder rig: under the left armpit, flat against the side of the chest, muzzle back into the jacket, grip forward
# (the slide/grip sit just outside the shirt; the jacket covers it - Unity hides it while the jacket is closed)
SH_F = V(-0.25, 1, -0.4).normalized(); SH_U = V(0, 0.4, 1).normalized()
SHOULDER_GUN = (V(0.172, -0.074, 1.315), SH_F, SH_U, V(-0.05, -0.35, 1.15))
BK_F = V(0.3, 0, -1).normalized(); BK_U = V(1, 0, 0.3).normalized()
BACK_GUN = (V(0.02, 0.117, 1.07), BK_F, BK_U, V(-0.55, 0.35, 1.25))
if 'Pistol_Back' not in bpy.data.objects:
    BELT_FRAME = frame_mat(G0N, FwN, UwN)
    for tag, (P, f, u, _pole), leather, bone in (('Shoulder', SHOULDER_GUN, True, 'Chest'), ('Back', BACK_GUN, False, 'Hips')):
        T = frame_mat(P - u * 0.017, f, u) @ BELT_FRAME.inverted()
        srcs = [('Pistol_Holster', 'Pistol_' + tag), ('Pistol_Holster_Grips', 'Pistol_' + tag + '_Grips')]
        if leather: srcs.append(('Holster', 'Holster_' + tag))
        for src, dst in srcs:
            o = bpy.data.objects[src]
            n = o.copy(); n.data = o.data.copy(); n.name = dst; n.data.name = dst; CHAR.objects.link(n)
            n.data.transform(T); n.data.update()
            weight_all(n, bone)

# ------------------------------------------------------------------ pose maths
XA, YA, ZA = (1, 0, 0), (0, 1, 0), (0, 0, 1)
D_R = tuple(arm_frame(-1)[1]); D_L = tuple(arm_frame(1)[1])
MR = {b.name: b.matrix_local.to_3x3() for b in rig.data.bones}
REST = {b.name: b.matrix_local.copy() for b in rig.data.bones}

def mirror_axis(a): return (a[0], -a[1], -a[2])
def mirror_name(n):
    return n[:-2] + '.R' if n.endswith('.L') else (n[:-2] + '.L' if n.endswith('.R') else n)

class Pose:
    def __init__(s): s.rot = {}; s.loc = {}
    def add(s, bone, rots, mirror=False):
        if mirror:
            bone = mirror_name(bone); rots = [(mirror_axis(a), d) for a, d in rots]
        s.rot.setdefault(bone, []).extend(rots); return s
    def hips(s, v): s.loc['Hips'] = Vector(v); return s
    def quats(s):
        out = {}
        for b, rots in s.rot.items():
            q = Quaternion()
            for a, d in rots: q = Quaternion(Vector(a).normalized(), radians(d)) @ q
            Mr = MR[b]; out[b] = (Mr.inverted() @ q.to_matrix() @ Mr).to_quaternion()
        locs = {b: MR[b].inverted() @ v for b, v in s.loc.items()}
        return out, locs

def mirrored(p):
    m = Pose()
    for b, rots in p.rot.items(): m.add(b, rots, mirror=True)
    for b, v in p.loc.items(): m.loc[b] = Vector((-v.x, v.y, v.z))
    return m

FINGERS = ('Index', 'Middle', 'Ring', 'Pinky')
def curl_hand(p, side, amounts, thumb=0.0):
    ax = (0, 1, 0) if side == 'L' else (0, -1, 0)
    for fn, (a1, a2, a3) in zip(FINGERS, amounts):
        p.add('%s1.%s' % (fn, side), [(ax, a1)]); p.add('%s2.%s' % (fn, side), [(ax, a2)]); p.add('%s3.%s' % (fn, side), [(ax, a3)])
    if thumb:
        p.add('Thumb2.%s' % side, [(ax, thumb)]); p.add('Thumb3.%s' % side, [(ax, thumb)])
    return p

RELAX_CURL = [(8, 12, 8), (10, 15, 10), (12, 18, 12), (14, 20, 14)]
GRAB_CURL = [(45, 55, 35), (50, 60, 38), (52, 62, 40), (55, 62, 40)]

def relaxed_arm(p, side, swing=0.0, elbow=12.0, out=70.0, curl=RELAX_CURL):
    if side == 'L':
        p.add('Shoulder.L', [(YA, 6)]).add('UpperArm.L', [(YA, out), (ZA, -6), (XA, swing)])
        p.add('ForeArm.L', [(XA, -elbow)]).add('Hand.L', [(XA, -4), (YA, 6)])
    else:
        p.add('Shoulder.R', [(YA, -6)]).add('UpperArm.R', [(YA, -out), (ZA, 6), (XA, swing)])
        p.add('ForeArm.R', [(XA, -elbow)]).add('Hand.R', [(XA, -4), (YA, -6)])
    curl_hand(p, side, curl)
    return p

AIM_CURL = [(22, 30, 20), (78, 88, 55), (82, 88, 55), (85, 80, 50)]
def aim_arm(p, pitch=0.0, recoil=0.0):
    p.add('Shoulder.R', [(ZA, 8)])
    p.add('UpperArm.R', [(ZA, 74), (XA, -9 - pitch - recoil)])
    p.add('ForeArm.R', [(D_R, 60)]).add('Hand.R', [(D_R, 30), (ZA, -2)])
    curl_hand(p, 'R', AIM_CURL, thumb=25)
    return p

# leg forward kinematics (hips drop so the lowest foot touches the ground)
L_TH, L_SH = 0.45, 0.415
def leg_h(th, kn, ab=0.0):
    return (L_TH * cos(radians(th)) + L_SH * cos(radians(th + kn))) * cos(radians(ab))
H0 = L_TH + L_SH

def leg(p, side, th, kn, pitch, ab=0.0, toe=0.0):
    sg = 1 if side == 'L' else -1
    p.add('Thigh.' + side, [(XA, th), (YA, -ab * sg)])
    p.add('Shin.' + side, [(XA, kn)])
    p.add('Foot.' + side, [(XA, -(th + kn) + pitch), (YA, ab * sg)])
    if toe: p.add('Toe.' + side, [(XA, toe)])
    return p

# ------------------------------------------------------------------ IK helpers (armature space == world, rig at origin)
def pbh(n): return rig.pose.bones[n].head.copy()
def rot_bone_world(n, Q):
    pb = rig.pose.bones[n]; Mx = pb.matrix.copy(); loc = Mx.to_translation()
    R = Q.to_matrix() @ Mx.to_3x3()
    pb.matrix = Matrix.Translation(loc) @ R.to_4x4(); upd()
def set_bone_rot(n, R3):
    pb = rig.pose.bones[n]; loc = pb.matrix.to_translation()
    pb.matrix = Matrix.Translation(loc) @ R3.to_4x4(); upd()
def two_bone(up, lo, hand, target, pole):
    A = pbh(up); B = pbh(lo); C = pbh(hand)
    la = (B - A).length; lb = (C - B).length
    T = target - A; lt = clamp(T.length, 0.02, (la + lb) * 0.999); ax = T.normalized()
    bend = pole - A; bend = bend - ax * bend.dot(ax); bend.normalize()
    ang = math.acos(clamp((la * la + lt * lt - lb * lb) / (2 * la * lt), -1, 1))
    E = A + ax * (cos(ang) * la) + bend * (sin(ang) * la)
    rot_bone_world(up, (B - A).rotation_difference(E - A))
    B2 = pbh(lo); C2 = pbh(hand)
    rot_bone_world(lo, (C2 - B2).rotation_difference((A + ax * lt) - B2))
def frame3(f, a, n): return Matrix((f, a, n)).transposed()
def ortho(f, n):
    f = f.normalized(); a = n.cross(f).normalized(); n2 = f.cross(a); return f, a, n2

# gun frame in rest
GF_REST = frame3(*ortho(D, Lh))           # columns: fwd, ., up  (we only need a consistent basis)
OFF_R = REST['Hand.R'].inverted() @ REST['Gun.R']
def place_gun(P, fwd, up, pole=None):
    """solve the right arm so Gun.R (bore rear) sits at P pointing along fwd with slide-top 'up'"""
    Q = frame3(*ortho(fwd, up)) @ GF_REST.transposed()
    Gd = Matrix.Translation(P) @ (Q @ REST['Gun.R'].to_3x3()).to_4x4()
    Hd = Gd @ OFF_R.inverted()
    two_bone('UpperArm.R', 'ForeArm.R', 'Hand.R', Hd.to_translation(), pole or V(-0.55, 0.25, 1.05))
    set_bone_rot('Hand.R', Hd.to_3x3())
LH_F_REST = REST['Hand.L'].col[1].xyz.normalized()
LH_N_REST = -arm_frame(1)[2]
LH_REST_FRAME = frame3(*ortho(LH_F_REST, LH_N_REST))
def place_left(palm, fdir, ndir, pole=None):
    f, a, n = ortho(fdir, ndir)
    wrist = palm - f * 0.05 - n * 0.016
    two_bone('UpperArm.L', 'ForeArm.L', 'Hand.L', wrist, pole or V(0.55, 0.25, 1.0))
    Q = frame3(f, a, n) @ LH_REST_FRAME.transposed()
    set_bone_rot('Hand.L', Q @ REST['Hand.L'].to_3x3())
def gun_frame():
    G = pbh('Gun.R'); f = (pbh('Muzzle.R') - G).normalized()
    u = pbh('GunUp.R') - G; u = (u - f * u.dot(f)).normalized(); s = f.cross(u)
    g = (f * -sin(G18) + u * -cos(G18)).normalized(); gpv = (f * cos(G18) + u * -sin(G18)).normalized()
    base = G - u * 0.017 + f * 0.004 + u * -0.012 + g * 0.105
    return G, f, u, s, g, gpv, base

def reset_pose():
    for pb in rig.pose.bones:
        pb.rotation_quaternion = Quaternion(); pb.location = Vector(); pb.scale = Vector((1, 1, 1))
def apply_frame(qs, ls):
    for pb in rig.pose.bones:
        pb.rotation_quaternion = qs.get(pb.name, Quaternion()); pb.location = ls.get(pb.name, Vector())
def capture():
    return ({pb.name: pb.rotation_quaternion.copy() for pb in rig.pose.bones},
            {'Hips': rig.pose.bones['Hips'].location.copy()})

# ------------------------------------------------------------------ clips
def idle(t):
    w = 2 * pi * t
    p = Pose()
    relaxed_arm(p, 'L', swing=1.5 * sin(w)); relaxed_arm(p, 'R', swing=-1.5 * sin(w + 0.5))
    p.add('Chest', [(XA, -1.6 * sin(2 * w))]).add('Spine', [(XA, 1.0 + 0.6 * sin(2 * w)), (YA, 1.0 * sin(w))])
    p.add('Head', [(ZA, 7 * sin(w + 1.0)), (XA, 3 + 1.5 * sin(2 * w))]).add('Neck', [(YA, -1.2 * sin(w))])
    leg(p, 'L', -1 + 1.2 * sin(w), 3 + 1.5 * (1 + sin(w)), 0, ab=-1.5)
    leg(p, 'R', 1 - 1.2 * sin(w), 3 + 1.5 * (1 - sin(w)), 0, ab=-1.5)
    p.hips((0.008 * sin(w), 0, -0.004 - 0.003 * (1 + sin(2 * w)) * 0.5))
    return p

def gait(t, run=False, back=False):
    phi = (1 - t) % 1.0 if back else t
    p = Pose(); hs = []
    for side, off in (('L', 0.0), ('R', 0.5)):
        q = (phi + off) % 1.0
        if run:
            th = -31 * cos(2 * pi * q) - 6
            kn = 24 + 92 * gp(q, 0.66, 0.15) + 14 * gp(q, 0.12, 0.06)
            pitch = -8 * gp(q, 0.0, 0.05) + 30 * gp(q, 0.48, 0.07) - 12 * gp(q, 0.8, 0.1)
        else:
            th = -20 * cos(2 * pi * q) + 1
            kn = 9 + 50 * gp(q, 0.72, 0.12) + 14 * gp(q, 0.10, 0.07)
            pitch = -15 * gp(q, 0.0, 0.05) + 26 * gp(q, 0.56, 0.06) - 4 * gp(q, 0.78, 0.1)
        toe = -24 * gp(q, 0.55, 0.06)
        leg(p, side, th, kn, pitch, ab=-4.5, toe=toe)
        hs.append(leg_h(th, kn, -4.5))
    c = cos(2 * pi * phi)
    drop = max(hs) - H0
    if run:
        drop += 0.028 * max(0.0, -cos(4 * pi * phi))
        p.add('Spine', [(XA, 12), (ZA, 4 * c)]).add('Chest', [(XA, 5), (ZA, 5 * c)])
        p.add('Head', [(XA, -15), (ZA, -3 * c)]).add('Hips', [(ZA, -7 * c), (YA, 1.5 * sin(2 * pi * phi))])
        relaxed_arm(p, 'L', swing=30 * c, elbow=95, out=77, curl=GRAB_CURL)
        relaxed_arm(p, 'R', swing=-30 * c, elbow=95, out=77, curl=GRAB_CURL)
        p.hips((0.004 * sin(2 * pi * phi), 0, drop))
    else:
        p.add('Spine', [(XA, 2), (ZA, 2.5 * c)]).add('Chest', [(ZA, 3 * c)])
        p.add('Head', [(ZA, -2 * c), (XA, 2)]).add('Hips', [(ZA, -4 * c), (YA, 1.2 * sin(2 * pi * phi))])
        relaxed_arm(p, 'L', swing=16 * c, elbow=10 + 12 * max(0, -c)); relaxed_arm(p, 'R', swing=-16 * c, elbow=10 + 12 * max(0, c))
        p.hips((0.005 * sin(2 * pi * phi), 0, drop))
    return p

def strafe(t):  # moving toward character's left (+X)
    p = Pose(); w = 2 * pi * t
    aL = 13 * (0.5 - 0.5 * cos(w)); aR = -13 * (0.5 - 0.5 * cos(w - pi * 0.6))
    kL = 6 + 30 * max(0.0, sin(w)); kR = 6 + 30 * max(0.0, sin(w - pi * 0.6))
    leg(p, 'L', -3 * sin(w) - kL * 0.35, kL, 0, ab=aL - 3)
    leg(p, 'R', -3 * sin(w) - kR * 0.35, kR, 0, ab=-aR - 3)
    drop = max(leg_h(-kL * 0.35, kL, aL), leg_h(-kR * 0.35, kR, aR)) - H0
    relaxed_arm(p, 'L', swing=4 * sin(w)); relaxed_arm(p, 'R', swing=-4 * sin(w))
    p.add('Hips', [(YA, 2 * sin(w))]).add('Spine', [(YA, -1.5 * sin(w))])
    p.hips((0.015 * sin(w), 0, drop))
    return p

def aim_pose(recoil=0.0, pitch=0.0):
    p = Pose()
    aim_arm(p, pitch=pitch, recoil=recoil)
    relaxed_arm(p, 'L', elbow=10)
    p.add('Chest', [(ZA, 10), (XA, -recoil * 0.35)]).add('Spine', [(ZA, 4)])
    p.add('Head', [(ZA, -12), (XA, 4)]).add('Neck', [(ZA, -3)])
    return p

def jump_keys():
    def crouch(depth, arms):
        p = Pose(); th = -38 * depth; kn = 70 * depth
        leg(p, 'L', th, kn, 0, ab=-3); leg(p, 'R', th, kn, 0, ab=-3)
        p.hips((0, 0, leg_h(th, kn) - H0)); p.add('Spine', [(XA, 16 * depth)]).add('Head', [(XA, -10 * depth)])
        relaxed_arm(p, 'L', swing=arms, elbow=20); relaxed_arm(p, 'R', swing=arms, elbow=20)
        return p
    def air(tuck):
        p = Pose(); leg(p, 'L', -45 * tuck, 60 * tuck, 15); leg(p, 'R', -25 * tuck, 75 * tuck, 15)
        p.add('Spine', [(XA, 6)]); relaxed_arm(p, 'L', swing=-25, elbow=35, out=50); relaxed_arm(p, 'R', swing=-15, elbow=35, out=50)
        return p
    ext = Pose(); leg(ext, 'L', 4, 2, 25); leg(ext, 'R', 6, 2, 25); relaxed_arm(ext, 'L', swing=-35, elbow=20, out=45); relaxed_arm(ext, 'R', swing=-35, elbow=20, out=45)
    return [(0, idle(0)), (5, crouch(0.6, 25)), (9, ext), (14, air(1.0)), (20, air(0.6)), (23, crouch(0.5, 10)), (30, idle(0))]

def shoot_keys():
    return [(0, aim_pose()), (1, aim_pose(recoil=11)), (3, aim_pose(recoil=7)), (8, aim_pose(recoil=0.5)), (10, aim_pose())]

def solve(base, gun=None, left=None):
    """FK base pose + optional right-arm gun placement + optional left-hand target -> captured key"""
    reset_pose(); apply_frame(*base.quats()); upd()
    if gun: place_gun(*gun)
    if left: place_left(*left(gun_frame()))
    return capture()

# ---- reload (115 frames = 3.833 s; Unity time-scales it to the reload duration)
RELOAD_LEN = 115
def rl_base(left_curl=RELAX_CURL, head_down=16):
    p = Pose(); aim_arm(p)
    relaxed_arm(p, 'L', elbow=12, curl=left_curl)
    p.add('Head', [(XA, head_down), (ZA, -4)]).add('Chest', [(XA, 4)])
    return p
def roll_up(fwd, deg):
    up = V(0, 0, 1); up = (up - fwd * up.dot(fwd)).normalized()
    return Quaternion(fwd, radians(deg)) @ up
# the pistol is canted inward: slide top toward the shooter's right, magwell facing the left hand coming from below
F1 = V(0.10, -1, 0.26).normalized()
GUN_RL1 = (V(-0.045, -0.42, 1.505), F1, roll_up(F1, 45))
F2 = V(0.28, -1, 0.14).normalized()
GUN_RL2 = (V(-0.02, -0.43, 1.50), F2, roll_up(F2, 14))
def L_pouch(gf): return (V(0.138, -0.114, 0.978), V(0, 0.1, -1), V(0, 1, 0))   # left jacket pocket
def L_lift(gf): return (V(0.10, -0.27, 1.25), V(-0.25, -0.95, 0.15), V(-0.2, 0.0, 1.0))
def L_below(gf):
    G, f, u, s, g, gpv, base = gf; return (base + g * 0.118, gpv, -g)
def L_insert(gf):
    G, f, u, s, g, gpv, base = gf; return (base + g * 0.018, gpv, -g)
def L_slap(gf):
    G, f, u, s, g, gpv, base = gf; return (base + g * 0.008, gpv, -g)
def L_rack(gf):
    G, f, u, s, g, gpv, base = gf; return (G + u * 0.030 + f * 0.035, s * 0.85 - u * 0.5, -u)
def L_racked(gf):
    G, f, u, s, g, gpv, base = gf; return (G + u * 0.030 + f * -0.012, s * 0.85 - u * 0.5, -u)
def L_release(gf):
    G, f, u, s, g, gpv, base = gf; return (G + u * 0.075 - s * 0.05 + f * -0.02, s * 0.6 - u * 0.5, -u)
INSERT_FRAME = 68
def reload_keys():
    k = []
    k.append((0, aim_pose().quats()))
    k.append((10, solve(rl_base(), GUN_RL1)))
    k.append((18, solve(rl_base(), GUN_RL1)))
    k.append((30, solve(rl_base(), GUN_RL1, L_pouch)))
    k.append((36, solve(rl_base(GRAB_CURL), GUN_RL1, L_pouch)))
    k.append((46, solve(rl_base(GRAB_CURL), GUN_RL1, L_lift)))
    k.append((56, solve(rl_base(GRAB_CURL), GUN_RL1, L_below)))
    k.append((INSERT_FRAME, solve(rl_base(GRAB_CURL), GUN_RL1, L_insert)))
    k.append((72, solve(rl_base(RELAX_CURL), GUN_RL1, L_slap)))
    k.append((76, solve(rl_base(RELAX_CURL), GUN_RL1, L_insert)))
    k.append((84, solve(rl_base(GRAB_CURL, 10), GUN_RL2, L_rack)))
    k.append((90, solve(rl_base(GRAB_CURL, 10), GUN_RL2, L_racked)))
    k.append((93, solve(rl_base(GRAB_CURL, 10), GUN_RL2, L_racked)))
    k.append((97, solve(rl_base(RELAX_CURL, 8), GUN_RL2, L_release)))
    k.append((106, aim_pose().quats()))
    k.append((RELOAD_LEN, aim_pose().quats()))
    return k

def keyed(keys):
    """keys: list of (frame, Pose | (quats, locs)) -> function(frame)->(quats, locs)"""
    qs = []
    for f, p in keys:
        qs.append((f, *(p.quats() if isinstance(p, Pose) else p)))
    bones = set(); [bones.update(q.keys()) for _, q, _ in qs]
    def at(fr):
        for i in range(len(qs) - 1):
            f0, q0, l0 = qs[i]; f1, q1, l1 = qs[i + 1]
            if f0 <= fr <= f1:
                t = (fr - f0) / max(1e-6, f1 - f0); t = t * t * (3 - 2 * t)
                outq = {b: q0.get(b, Quaternion()).slerp(q1.get(b, Quaternion()), t) for b in bones}
                outl = {b: l0.get(b, Vector()).lerp(l1.get(b, Vector()), t) for b in set(l0) | set(l1)}
                return outq, outl
        f, q, l = qs[-1]; return q, l
    return at

def from_pose_fn(fn, n):
    return lambda fr: fn(fr / n).quats()

RELOAD = reload_keys()

# ---- carry modes: draw / holster. At the swap frame the hand-held pistol coincides with the carried one.
FL = V(0.15, -1, -0.95).normalized()
GUN_LOWREADY = (V(-0.165, -0.30, 1.10), FL, roll_up(FL, 0))
FD = V(0.0, -1, -0.35).normalized()
GUN_DRAWFWD = (V(-0.21, -0.12, 1.24), FD, roll_up(FD, 0))
FX = V(0.15, -1, -0.2).normalized()
GUN_XFWD = (V(-0.08, -0.30, 1.25), FX, roll_up(FX, 0), V(-0.4, -0.1, 1.05))
FS = V(0.0, -1, -0.7).normalized()
GUN_SIDE = (V(-0.27, -0.04, 1.05), FS, roll_up(FS, 0), V(-0.55, 0.3, 1.2))
def hol_offset(up_amount, back=0.0):
    P, f, u, pole = HOLSTER_GUN
    return (P - f * up_amount + V(0, back, 0), f, u, pole)
def offset_gun(g, d):
    P, f, u, pole = g
    return (P + d, f, u, pole)
def draw_base(curl, lcurl=RELAX_CURL):
    p = Pose(); relaxed_arm(p, 'L', elbow=12, curl=lcurl); relaxed_arm(p, 'R', elbow=20, curl=curl)
    p.add('Head', [(XA, 4)])
    return p
def armed_base(lcurl=RELAX_CURL):
    p = Pose(); aim_arm(p); relaxed_arm(p, 'L', elbow=12, curl=lcurl); return p
def lerp_gun(a, b, t):
    return (a[0].lerp(b[0], t), a[1].lerp(b[1], t).normalized(), a[2].lerp(b[2], t).normalized(), a[3] if len(a) > 3 else None)
# left hand while drawing from under the jacket: pulls the left lapel open, or lifts the back hem and holds it at the waist
LAPEL_PT = V(0.070, -0.140, 1.255)    # just outside the left lapel edge, chest height
HEM_GRAB = V(0.12, 0.150, 0.880)      # back hem, left of the gun (where the fingers hook under it)
HEM_HOLD = V(0.12, 0.150, 0.900)      # the gathered hem the hand pinches at the waist once the jacket is pulled up
def L_lapel(o):
    q = Quaternion(V(0, 0, 1), radians(JF_DEG * o))
    return (flap_front(LAPEL_PT, o), q @ V(-0.45, 0.05, -0.89), q @ V(0.1, 1, 0), V(0.6, 0.15, 1.05))
def L_hem(o):
    # closed: fingers hook under the hem; rolled up: the hand pinches the fold against the small of the back
    p = HEM_GRAB.lerp(flap_back(HEM_HOLD, 1.0) + V(0, 0.02, 0), o) if o < 1.0 else flap_back(HEM_HOLD, 1.0) + V(0, 0.02, 0)
    f = V(-0.2, 0.25, -0.95).lerp(V(-0.4, 0.2, -0.9), o); n = V(0.0, -1, 0.15).lerp(V(0.0, -1, 0.1), o)
    return (p, f, n, V(0.5, 0.45, 1.25))
def pull_up(g, d, side=0.0, back=0.0):
    """the tucked pistol pulled straight up out of the waistband along its bore (muzzle points down)"""
    P, f, u, pole = g
    return (P - f * d + V(-side, back, 0), f, u, pole)
GUN_SH_OUT = lerp_gun(GUN_XFWD, GUN_LOWREADY, 0.45)
GUN_BK_OUT = lerp_gun(GUN_SIDE, GUN_LOWREADY, 0.45)
# mode: (length, draw swap frame, keys). key = (frame, right hand, right target, left hand, jacket front open, back open)
# The flap timelines (frames + open values) are mirrored in VincePlayer.cs (JacketKeys) - keep them in sync.
CARRY = {
    'Belt': (16, 6, [(0, 'relaxed', None, None, 0, 0), (3, 'open', hol_offset(0.07, 0.03), None, 0, 0), (6, 'grip', HOLSTER_GUN, None, 0, 0),
                     (9, 'grip', hol_offset(0.17), None, 0, 0), (12, 'grip', GUN_DRAWFWD, None, 0, 0), (16, 'grip', GUN_LOWREADY, None, 0, 0)]),
    'Shoulder': (50, 24, [(0, 'relaxed', None, None, 0, 0),
                          (7, 'relaxed', None, ('lapel', 0.0), 0, 0),
                          (14, 'open', offset_gun(SHOULDER_GUN, V(-0.05, -0.16, 0.0)), ('lapel', 1.0), 1, 0),
                          (19, 'open', offset_gun(SHOULDER_GUN, V(-0.02, -0.07, 0.02)), ('lapel', 1.0), 1, 0),
                          (24, 'grip', SHOULDER_GUN, ('lapel', 1.0), 1, 0),
                          (31, 'grip', offset_gun(SHOULDER_GUN, V(-0.03, -0.17, -0.01)), ('lapel', 1.0), 1, 0),
                          (36, 'grip', GUN_XFWD, ('lapel', 0.6), 0.6, 0),
                          (41, 'grip', GUN_SH_OUT, None, 0, 0),
                          (50, 'grip', GUN_LOWREADY, None, 0, 0)]),
    # slowest: the left hand rolls the jacket up, the right takes the pistol and pulls it straight UP out of the waistband
    'Back': (66, 33, [(0, 'relaxed', None, None, 0, 0),
                      (10, 'relaxed', None, ('hem', 0.0), 0, 0),
                      (20, 'relaxed', None, ('hem', 1.0), 0, 1),
                      (27, 'open', offset_gun(BACK_GUN, V(-0.10, 0.07, 0.10)), ('hem', 1.0), 0, 1),
                      (33, 'grip', BACK_GUN, ('hem', 1.0), 0, 1),
                      (40, 'grip', pull_up(BACK_GUN, 0.10), ('hem', 1.0), 0, 1),
                      (46, 'grip', pull_up(BACK_GUN, 0.20, 0.08, 0.0), ('hem', 1.0), 0, 1),
                      (52, 'grip', GUN_SIDE, ('hem', 0.4), 0, 0.4),
                      (58, 'grip', GUN_BK_OUT, None, 0, 0),
                      (66, 'grip', GUN_LOWREADY, None, 0, 0)]),
}
def seq_keys(steps):
    out = []
    for fr, kind, tgt, left, jf, jb in steps:
        lcurl = GRAB_CURL if left else RELAX_CURL
        base = draw_base(RELAX_CURL, lcurl) if kind in ('relaxed', 'open') else armed_base(lcurl)
        jacket_pose(base, jf, jb)
        lf = None
        if left: lf = (lambda gf, k=left[0], o=left[1]: L_lapel(o) if k == 'lapel' else L_hem(o))
        if kind == 'relaxed' and not lf: out.append((fr, base.quats()))
        else: out.append((fr, solve(base, tgt if kind != 'relaxed' else None, lf)))
    return out
def jacket_open_at(steps, fr):
    """front / back flap opening at a frame (same smoothstep interpolation as keyed())"""
    for i in range(len(steps) - 1):
        f0, f1 = steps[i][0], steps[i + 1][0]
        if f0 <= fr <= f1:
            t = (fr - f0) / max(1e-6, f1 - f0); t = t * t * (3 - 2 * t)
            return (steps[i][4] + (steps[i + 1][4] - steps[i][4]) * t, steps[i][5] + (steps[i + 1][5] - steps[i][5]) * t)
    return steps[-1][4], steps[-1][5]
DRAW_CLIPS = []
for mode, (length, swap, steps) in CARRY.items():
    sfx = '' if mode == 'Belt' else mode
    DRAW_CLIPS.append(('Draw' + sfx, length, keyed(seq_keys(steps))))
    rev = [(length - s[0],) + tuple(s[1:]) for s in reversed(steps)]
    DRAW_CLIPS.append(('Holster' + sfx, length, keyed(seq_keys(rev))))
    print('CARRY', mode, 'len', length, 'drawSwap', swap, 'holsterSwap', length - swap,
          'jacket keys', [(s[0], s[4], s[5]) for s in steps])

# ---- stealth draws (C pressed once): slow and quiet; the pistol ends up concealed instead of at low ready.
# belt: the hand only goes to the holstered pistol and rests on it (the quick draw later starts from there)
# shoulder: out of the jacket, then held muzzle-down at belt level against the right thigh
# back: out of the waistband, then kept behind the back
HIDE_SHOULDER = (V(-0.215, -0.075, 1.00), V(0, 0.12, -1).normalized(), V(0, -1, -0.12).normalized(), V(-0.45, 0.25, 1.15))
HIDE_BACK = pull_up(BACK_GUN, 0.09, 0.02, 0.07)
_SH_OUT = offset_gun(SHOULDER_GUN, V(-0.03, -0.12, -0.06))
CARRY_STEALTH = {   # mode: (length, swap frame or None, keys) - jacket keys mirrored in VincePlayer.cs
    'Belt': (24, None, [(0, 'relaxed', None, None, 0, 0), (12, 'open', hol_offset(0.07, 0.03), None, 0, 0), (24, 'grip', HOLSTER_GUN, None, 0, 0)]),
    'Shoulder': (64, 30, [(0, 'relaxed', None, None, 0, 0),
                          (9, 'relaxed', None, ('lapel', 0.0), 0, 0),
                          (18, 'open', offset_gun(SHOULDER_GUN, V(-0.05, -0.16, 0.0)), ('lapel', 1.0), 1, 0),
                          (24, 'open', offset_gun(SHOULDER_GUN, V(-0.02, -0.07, 0.02)), ('lapel', 1.0), 1, 0),
                          (30, 'grip', SHOULDER_GUN, ('lapel', 1.0), 1, 0),
                          (38, 'grip', _SH_OUT, ('lapel', 1.0), 1, 0),
                          (47, 'grip', lerp_gun(_SH_OUT, HIDE_SHOULDER, 0.55), ('lapel', 0.5), 0.5, 0),
                          (55, 'grip', HIDE_SHOULDER, None, 0, 0),
                          (64, 'grip', HIDE_SHOULDER, None, 0, 0)]),
    'Back': (80, 40, [(0, 'relaxed', None, None, 0, 0),
                      (12, 'relaxed', None, ('hem', 0.0), 0, 0),
                      (24, 'relaxed', None, ('hem', 1.0), 0, 1),
                      (32, 'open', offset_gun(BACK_GUN, V(-0.10, 0.07, 0.10)), ('hem', 1.0), 0, 1),
                      (40, 'grip', BACK_GUN, ('hem', 1.0), 0, 1),
                      (50, 'grip', pull_up(BACK_GUN, 0.09), ('hem', 1.0), 0, 1),
                      (60, 'grip', HIDE_BACK, ('hem', 0.5), 0, 0.5),
                      (70, 'grip', HIDE_BACK, None, 0, 0),
                      (80, 'grip', HIDE_BACK, None, 0, 0)]),
}
STEALTH_CLIPS = []
for mode, (length, swap, steps) in CARRY_STEALTH.items():
    sfx = '' if mode == 'Belt' else mode
    keys = seq_keys(steps)
    STEALTH_CLIPS.append(('Draw' + sfx + 'Stealth', length, keyed(keys)))
    rev = [(length - s[0],) + tuple(s[1:]) for s in reversed(steps)]
    STEALTH_CLIPS.append(('Holster' + sfx + 'Stealth', length, keyed(seq_keys(rev))))
    STEALTH_CLIPS.append(('Hide' + mode, 60, keyed([(0, keys[-1][1]), (60, keys[-1][1])])))
    print('STEALTH', mode, 'len', length, 'swap', swap, 'jacket keys', [(s[0], s[4], s[5]) for s in steps])

# ---- pistol in hand, not aiming: low ready with breathing, and a bob while walking
def armed_key(dP, pitch, yaw_):
    P, f, u = GUN_LOWREADY
    side = f.cross(V(0, 0, 1)).normalized()
    q = Quaternion(V(0, 0, 1), radians(yaw_)) @ Quaternion(side, radians(pitch))
    return solve(armed_base(), (P + dP, q @ f, q @ u))
ARMED_IDLE = [(fr, armed_key(V(0, 0, 0.005 * sin(2 * pi * fr / 90)), 1.8 * sin(2 * pi * fr / 90 + 0.6), 1.0 * sin(2 * pi * fr / 45)))
              for fr in range(0, 91, 15)]
ARMED_WALK = [(fr, armed_key(V(0.006 * sin(2 * pi * fr / 32), 0.004 * cos(4 * pi * fr / 32), 0.012 * abs(sin(2 * pi * fr / 32)) - 0.006),
                             3 * sin(4 * pi * fr / 32), 2 * sin(2 * pi * fr / 32))) for fr in range(0, 33, 4)]

# ---- crouch
def crouch(t, walk=False):
    p = Pose(); w = 2 * pi * t; hs = []
    for side, off in (('L', 0.0), ('R', 0.5)):
        q = (t + off) % 1.0
        if walk:
            th = -66 - 16 * cos(2 * pi * q); kn = 100 + 26 * gp(q, 0.7, 0.14)
        else:
            th = -66 + 1.0 * sin(w); kn = 102 + 1.5 * sin(w)
        leg(p, side, th, kn, 0, ab=-3)
        hs.append(leg_h(th, kn, -3))
    drop = max(hs) - H0
    c = cos(w)
    p.add('Spine', [(XA, 20)]).add('Chest', [(XA, 8 + 1.2 * sin(2 * w))]).add('Head', [(XA, -24)])
    relaxed_arm(p, 'L', swing=(10 * c if walk else 2 * sin(w)) - 10, elbow=40, out=72)
    relaxed_arm(p, 'R', swing=(-10 * c if walk else -2 * sin(w)) - 10, elbow=40, out=72)
    p.hips((0.01 * sin(w) if walk else 0.0, 0.06, drop))
    return p

# ---- jump in three parts: take-off, falling loop, landing
def j_crouch(depth, arms):
    p = Pose(); th = -38 * depth; kn = 70 * depth
    leg(p, 'L', th, kn, 0, ab=-3); leg(p, 'R', th, kn, 0, ab=-3)
    p.hips((0, 0, leg_h(th, kn) - H0)); p.add('Spine', [(XA, 16 * depth)]).add('Head', [(XA, -10 * depth)])
    relaxed_arm(p, 'L', swing=arms, elbow=20); relaxed_arm(p, 'R', swing=arms, elbow=20)
    return p
def j_air(tuck, ph=0.0):
    p = Pose(); leg(p, 'L', -45 * tuck + 5 * sin(ph), 60 * tuck, 15); leg(p, 'R', -25 * tuck - 5 * sin(ph), 75 * tuck, 15)
    p.add('Spine', [(XA, 6)])
    relaxed_arm(p, 'L', swing=-25 + 4 * sin(ph), elbow=35, out=50); relaxed_arm(p, 'R', swing=-15 - 4 * sin(ph), elbow=35, out=50)
    return p
def j_ext():
    p = Pose(); leg(p, 'L', 4, 2, 25); leg(p, 'R', 6, 2, 25)
    relaxed_arm(p, 'L', swing=-35, elbow=20, out=45); relaxed_arm(p, 'R', swing=-35, elbow=20, out=45)
    return p
JUMPSTART = [(0, idle(0)), (4, j_crouch(0.55, 25)), (9, j_ext()), (12, j_air(0.7))]
LAND = [(0, j_air(0.6)), (3, j_crouch(0.75, 15)), (8, j_crouch(0.4, 5)), (15, idle(0))]

# ---- bots: sitting at a table, hit reactions (fall in pain, writhe, get up, scoot to cover, sit against cover), staggering
def sit(t):
    w = 2 * pi * t; p = Pose()
    leg(p, 'L', -88, 86, 0, ab=-6); leg(p, 'R', -86, 88, 0, ab=-5)
    p.hips((0, 0, leg_h(-88, 86) - H0))
    p.add('Spine', [(XA, 6 + 1.0 * sin(w))]).add('Chest', [(XA, 2 + 1.2 * sin(2 * w))])
    p.add('Head', [(ZA, 12 * sin(w * 0.5)), (XA, 4)])
    relaxed_arm(p, 'L', swing=-38, elbow=78 + 3 * sin(w), out=70); relaxed_arm(p, 'R', swing=-34 + 4 * sin(w + 1), elbow=84, out=70)
    return p
def clutch_arms(p, k=1.0, ph=0.0):
    relaxed_arm(p, 'L', swing=-40 * k + 6 * sin(ph), elbow=100 * k + 8 * sin(ph), out=62, curl=GRAB_CURL)
    relaxed_arm(p, 'R', swing=-22 * k, elbow=75 * k + 6 * sin(ph + 1), out=66, curl=GRAB_CURL)
    return p
LIE_DROP = -0.86
def clutch_lying(p, ph=0.0):
    # lying down the arms stay along the body, forearms across the belly / chest (standing clutch angles would point
    # the arms at the ceiling)
    relaxed_arm(p, 'L', swing=-12 + 4 * sin(ph), elbow=118 + 6 * sin(ph), out=77, curl=GRAB_CURL)
    relaxed_arm(p, 'R', swing=-4, elbow=104 + 5 * sin(ph + 1), out=75, curl=GRAB_CURL)
    return p
def writhe(t):
    w = 2 * pi * t; p = Pose()
    # on the back, rolled toward one side, one knee drawn up, the other leg stretched and restless
    p.add('Hips', [(XA, -86 + 3 * sin(w)), (YA, 20 + 8 * sin(w))]); p.hips((0.02 * sin(w), 0.0, LIE_DROP))
    leg(p, 'L', -38 - 12 * sin(w), 74 + 16 * sin(w), 8); leg(p, 'R', -10 + 8 * sin(w + 1.5), 18 + 14 * sin(w + 1.5), 4)
    p.add('Spine', [(XA, 10 + 5 * sin(2 * w)), (YA, 6)]).add('Chest', [(XA, 6), (ZA, 6 * sin(w))])
    p.add('Head', [(ZA, 18 * sin(w * 0.5)), (XA, 10)]).add('Neck', [(XA, 8)])
    clutch_lying(p, w)
    return p
def hurt_bend():
    p = j_crouch(0.45, 0); p.add('Spine', [(XA, 28)]).add('Head', [(XA, 18)]); clutch_arms(p); return p
def kneel_hurt():
    p = Pose(); leg(p, 'L', -6, 96, 25); leg(p, 'R', -4, 94, 25)
    p.hips((0, 0.04, 0.51 - 0.98)); p.add('Spine', [(XA, 25)]).add('Head', [(XA, 15)]); clutch_arms(p); return p
def tip_back():
    p = Pose(); p.add('Hips', [(XA, -48), (YA, 18)]); p.hips((0, 0.10, -0.72))
    leg(p, 'L', -45, 85, 10); leg(p, 'R', -30, 70, 10); p.add('Spine', [(XA, 18)]).add('Head', [(XA, 20)]); clutch_arms(p); return p
DOWN_FALL = [(0, idle(0)), (5, hurt_bend()), (12, kneel_hurt()), (20, tip_back()), (30, writhe(0))]
def sit_up():
    p = Pose(); p.add('Hips', [(XA, -38)]); p.hips((0, 0.05, -0.87))
    leg(p, 'L', -62, 104, 0); leg(p, 'R', -50, 90, 0); p.add('Spine', [(XA, 14)])
    relaxed_arm(p, 'L', swing=45, elbow=15, out=70); relaxed_arm(p, 'R', swing=45, elbow=15, out=70); return p
def kneel_one():
    p = Pose(); leg(p, 'R', -5, 95, 25); leg(p, 'L', -85, 90, 0); p.hips((0, 0.02, 0.51 - 0.98))
    p.add('Spine', [(XA, 22)]); relaxed_arm(p, 'L', swing=-30, elbow=60, out=72); relaxed_arm(p, 'R', swing=-10, elbow=25, out=74); return p
GET_UP = [(0, writhe(0)), (14, sit_up()), (28, kneel_one()), (40, j_crouch(0.6, 10)), (54, idle(0))]
def scoot(t):
    w = 2 * pi * t; p = Pose()
    p.add('Hips', [(XA, -20)]); p.hips((0, 0, LIE_DROP))
    leg(p, 'L', -70, 15 + 32 * max(0.0, sin(w)), -10); leg(p, 'R', -70, 15 + 32 * max(0.0, sin(w + pi)), -10)
    p.add('Spine', [(XA, 18)]).add('Chest', [(XA, 6)]).add('Head', [(XA, 10)])
    relaxed_arm(p, 'L', swing=45 + 10 * sin(w), elbow=15, out=78); relaxed_arm(p, 'R', swing=45 + 10 * sin(w + pi), elbow=15, out=78)
    return p
def cover_sit(t):
    w = 2 * pi * t; p = Pose()
    p.add('Hips', [(XA, -12)]); p.hips((0, 0, LIE_DROP))
    leg(p, 'L', -120, 115, 0, ab=-8); leg(p, 'R', -108, 100, 0, ab=-6)
    p.add('Spine', [(XA, 10 + 1.5 * sin(w))]).add('Chest', [(XA, 4 + 1.5 * sin(2 * w))]).add('Head', [(ZA, 15 * sin(w * 0.5)), (XA, 6)])
    relaxed_arm(p, 'R', swing=-45, elbow=72, out=70); relaxed_arm(p, 'L', swing=-30, elbow=50, out=72)
    return p
def stagger(t):
    w = 2 * pi * t; p = gait(t)
    p.add('Spine', [(ZA, 9 * sin(w * 0.5 + 0.5)), (XA, 8)]).add('Chest', [(ZA, 6 * sin(w * 0.5))]).add('Hips', [(YA, 6 * sin(w * 0.5))])
    p.add('Head', [(XA, 16 + 6 * sin(w)), (ZA, 10 * sin(w * 0.5))])
    p.add('Shin.R', [(XA, 14 * gp(t, 0.3, 0.08))])
    return p
BOT_CLIPS = [('Sit', 120, from_pose_fn(sit, 120)), ('DownFall', 30, keyed(DOWN_FALL)), ('Writhe', 90, from_pose_fn(writhe, 90)),
             ('GetUp', 54, keyed(GET_UP)), ('Scoot', 36, from_pose_fn(scoot, 36)), ('CoverSit', 60, from_pose_fn(cover_sit, 60)),
             ('Stagger', 40, from_pose_fn(stagger, 40))]

# ---- smoking (left hand). Objects are placed exactly; the hand follows.
def place_obj(side, obj_rest, desired, pole):
    hand = 'Hand.' + side
    Hd = desired @ obj_rest.inverted() @ REST[hand]
    two_bone('UpperArm.' + side, 'ForeArm.' + side, hand, Hd.to_translation(), pole)
    set_bone_rot(hand, Hd.to_3x3())
def solve_s(base, left=None, right=None, lpalm=None):
    reset_pose(); apply_frame(*base.quats()); upd()
    if right: place_obj('R', *right)
    if left: place_obj('L', *left)
    if lpalm: place_left(*lpalm)
    return capture()
def moved(fr_, d):
    return Matrix.Translation(d) @ fr_
CIG_CURL = [(12, 18, 10), (15, 22, 12), (40, 55, 30), (45, 55, 30)]
def smoke_base(lcurl=CIG_CURL, rcurl=RELAX_CURL):
    p = Pose(); relaxed_arm(p, 'L', elbow=15, curl=lcurl); relaxed_arm(p, 'R', elbow=15, curl=rcurl); return p
PL_POCKET, PL_MOUTH, PL_HOLD = V(0.5, 0.2, 1.05), V(0.5, 0.1, 1.15), V(0.5, 0.25, 1.0)
PR_POCKET = V(-0.5, 0.2, 1.05)
PACK_POCKET = obj_frame(V(0.075, -0.068, 1.335), V(0, 0, 1), V(0, 1, -0.3))
PACK_MOUTH = obj_frame(MOUTH_C + V(0.0, -0.016, -0.012), V(0, 0.35, 1), V(-1, 0, 0))
PACK_LOW = obj_frame(MOUTH_C + V(0.03, -0.09, -0.12), V(0, 0.2, 1), V(-1, 0, 0))
LIGHTER_POCKET = obj_frame(V(-0.125, -0.072, 0.955), V(0, 0, 1), V(0, 1, -0.2))
LIGHTER_FIRE = obj_frame(CIG_E + V(0.0, -0.008, -0.032), V(0, 0, 1), V(1, 0, 0))
LIGHTER_LOW = obj_frame(CIG_E + V(-0.05, -0.12, -0.20), V(0, 0, 1), V(1, 0, 0))
CUP_L = (CIG_E + V(0.035, -0.012, -0.012), V(-0.2, 0, 1), V(-1, 0, 0))
CIG_AT_MOUTH = obj_frame(MOUTH_C + V(0.003, -0.004, 0.0), V(0.25, -1, -0.15), V(-0.35, 0, 1))
CIG_HOLD = obj_frame(V(0.25, -0.17, 1.02), V(0.9, -0.4, 0.25), V(0.1, -0.6, -0.8))
GRAB_PACK = [(35, 45, 30)] * 4
SMOKE_START = [
    (0, smoke_base(RELAX_CURL).quats()),
    (12, solve_s(smoke_base(GRAB_PACK), left=(PACK_REST, PACK_POCKET, PL_POCKET))),
    (16, solve_s(smoke_base(GRAB_PACK), left=(PACK_REST, PACK_POCKET, PL_POCKET))),
    (30, solve_s(smoke_base(GRAB_PACK), left=(PACK_REST, PACK_MOUTH, PL_MOUTH))),
    (38, solve_s(smoke_base(GRAB_PACK), left=(PACK_REST, PACK_MOUTH, PL_MOUTH))),
    (46, solve_s(smoke_base(GRAB_PACK), left=(PACK_REST, PACK_LOW, PL_MOUTH))),
    (58, solve_s(smoke_base(GRAB_PACK), left=(PACK_REST, PACK_POCKET, PL_POCKET))),
    (64, solve_s(smoke_base(RELAX_CURL, GRAB_PACK), right=(LIGHTER_REST, LIGHTER_POCKET, PR_POCKET))),
    (68, solve_s(smoke_base(RELAX_CURL, GRAB_PACK), right=(LIGHTER_REST, LIGHTER_POCKET, PR_POCKET))),
    (80, solve_s(smoke_base(RELAX_CURL, GRAB_PACK), right=(LIGHTER_REST, LIGHTER_FIRE, PR_POCKET), lpalm=CUP_L)),
    (98, solve_s(smoke_base(RELAX_CURL, GRAB_PACK), right=(LIGHTER_REST, LIGHTER_FIRE, PR_POCKET), lpalm=CUP_L)),
    (106, solve_s(smoke_base(RELAX_CURL, GRAB_PACK), right=(LIGHTER_REST, LIGHTER_LOW, PR_POCKET))),
    (116, solve_s(smoke_base(RELAX_CURL, GRAB_PACK), right=(LIGHTER_REST, LIGHTER_POCKET, PR_POCKET))),
    (124, smoke_base(RELAX_CURL).quats()),
    (135, smoke_base(RELAX_CURL).quats())]
SMOKE_LOOP = [(fr, solve_s(smoke_base(), left=(HANDCIG_REST, tgt, pole))) for fr, tgt, pole in (
    (0, CIG_AT_MOUTH, PL_MOUTH), (22, CIG_AT_MOUTH, PL_MOUTH), (44, CIG_HOLD, PL_HOLD), (118, CIG_HOLD, PL_HOLD),
    (123, moved(CIG_HOLD, V(0, 0, -0.03)), PL_HOLD), (128, CIG_HOLD, PL_HOLD), (198, CIG_HOLD, PL_HOLD),
    (220, CIG_AT_MOUTH, PL_MOUTH), (240, CIG_AT_MOUTH, PL_MOUTH))]
SMOKE_THROW = [(0, solve_s(smoke_base(), left=(HANDCIG_REST, CIG_HOLD, PL_HOLD))),
               (7, solve_s(smoke_base(), left=(HANDCIG_REST, moved(CIG_HOLD, V(-0.02, 0.05, 0.08)), PL_HOLD))),
               (12, solve_s(smoke_base(RELAX_CURL), left=(HANDCIG_REST, moved(CIG_HOLD, V(0.06, -0.16, -0.06)), PL_HOLD))),
               (24, smoke_base(RELAX_CURL).quats())]
MOUTH_THROW = [(0, smoke_base(RELAX_CURL).quats()),
               (9, solve_s(smoke_base(), left=(HANDCIG_REST, MOUTHCIG_FRAME, PL_MOUTH))),
               (13, solve_s(smoke_base(), left=(HANDCIG_REST, moved(MOUTHCIG_FRAME, V(0.05, -0.10, -0.04)), PL_MOUTH))),
               (16, solve_s(smoke_base(RELAX_CURL), left=(HANDCIG_REST, moved(MOUTHCIG_FRAME, V(0.18, -0.28, -0.22)), PL_HOLD))),
               (24, smoke_base(RELAX_CURL).quats())]
# before drawing: the hand puts the cigarette between the lips (swap at frame 10), then drops away
CIG_TO_MOUTH = [(0, solve_s(smoke_base(), left=(HANDCIG_REST, CIG_HOLD, PL_HOLD))),
                (10, solve_s(smoke_base(), left=(HANDCIG_REST, MOUTHCIG_FRAME, PL_MOUTH))),
                (14, solve_s(smoke_base(RELAX_CURL), left=(HANDCIG_REST, moved(MOUTHCIG_FRAME, V(0.04, -0.06, -0.08)), PL_MOUTH))),
                (22, smoke_base(RELAX_CURL).quats())]
reset_pose(); upd()

# ---- magazine held in the left hand: exactly coincides with the gun's magazine at the insert frame
if 'Mag_InHand' not in bpy.data.objects:
    reset_pose(); apply_frame(*dict(RELOAD)[INSERT_FRAME]); upd()
    Gp = rig.pose.bones['Gun.R'].matrix.copy(); Hp = rig.pose.bones['Hand.L'].matrix.copy()
    T = REST['Hand.L'] @ Hp.inverted() @ Gp @ REST['Gun.R'].inverted()
    src = bpy.data.objects['Mag_InGun']
    mh = src.copy(); mh.data = src.data.copy(); mh.name = 'Mag_InHand'; mh.data.name = 'Mag_InHand'
    CHAR.objects.link(mh); mh.data.transform(T); mh.data.update()
    weight_all(mh, 'Hand.L')
    reset_pose(); upd()

CLIPS = [
    ('Idle', 120, from_pose_fn(idle, 120)),
    ('Walk', 32, from_pose_fn(lambda t: gait(t), 32)),
    ('Run', 22, from_pose_fn(lambda t: gait(t, run=True), 22)),
    ('WalkBack', 34, from_pose_fn(lambda t: gait(t, back=True), 34)),
    ('StrafeLeft', 30, from_pose_fn(strafe, 30)),
    ('StrafeRight', 30, from_pose_fn(lambda t: mirrored(strafe(t)), 30)),
    ('JumpStart', 12, keyed(JUMPSTART)),
    ('Fall', 24, from_pose_fn(lambda t: j_air(0.85, 2 * pi * t), 24)),
    ('Land', 15, keyed(LAND)),
    ('CrouchIdle', 60, from_pose_fn(lambda t: crouch(t), 60)),
    ('CrouchWalk', 36, from_pose_fn(lambda t: crouch(t, True), 36)),
    ('Aim', 60, from_pose_fn(lambda t: aim_pose(pitch=0.6 * sin(2 * pi * t)), 60)),
    ('Shoot', 10, keyed(shoot_keys())),
    ('Reload', RELOAD_LEN, keyed(RELOAD)),
    ('ArmedIdle', 90, keyed(ARMED_IDLE)),
    ('ArmedWalk', 32, keyed(ARMED_WALK)),
    ('SmokeStart', 135, keyed(SMOKE_START)),
    ('SmokeLoop', 240, keyed(SMOKE_LOOP)),
    ('SmokeThrow', 24, keyed(SMOKE_THROW)),
    ('MouthThrow', 24, keyed(MOUTH_THROW)),
    ('CigToMouth', 22, keyed(CIG_TO_MOUTH)),
] + DRAW_CLIPS + STEALTH_CLIPS + BOT_CLIPS

if not rig.animation_data: rig.animation_data_create()
for name, n, fn in CLIPS:
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    rig.animation_data.action = act
    for fr in range(n + 1):
        qs, ls = fn(fr)
        apply_frame(qs, ls)
        for pb in rig.pose.bones:
            pb.keyframe_insert('rotation_quaternion', frame=fr, group=pb.name)
            if pb.name == 'Hips': pb.keyframe_insert('location', frame=fr, group=pb.name)
    print('CLIP', name, n)

# ------------------------------------------------------------------ preview contact sheet
if PREVIEW:
    import numpy as np
    bpy.data.collections['Env_Alley'].hide_render = True
    cd = bpy.data.cameras.new('PrevCam'); cam = bpy.data.objects.new('PrevCam', cd); scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.engine = 'BLENDER_EEVEE'; scene.eevee.taa_render_samples = 16
    scene.view_settings.exposure = -0.4
    scene.render.resolution_x, scene.render.resolution_y = 360, 480
    def vis(name_prefix, on):
        for o in CHAR.objects:
            if o.name.startswith(name_prefix): o.hide_render = not on
    eye = V(0, -0.095, 1.735)
    shots = [('DrawStealth', 24, 'h'), ('HideBelt', 0, 'h'), ('DrawShoulderStealth', 38, 'q'), ('DrawShoulderStealth', 55, 'q'),
             ('HideShoulder', 0, 'h'), ('DrawBackStealth', 50, 'K'), ('HideBack', 0, 'K'), ('HideBack', 0, 'X'),
             ('Sit', 0, 's'), ('DownFall', 12, 'L'), ('Writhe', 20, 'L'), ('GetUp', 14, 'L'),
             ('GetUp', 28, 'L'), ('Scoot', 10, 'L'), ('CoverSit', 0, 'L'), ('Stagger', 10, 'f')]
    tiles = []
    for i, (cn, fr, view) in enumerate(shots):
        rig.animation_data.action = bpy.data.actions[cn]
        scene.frame_set(fr)
        mode = 'Shoulder' if 'Shoulder' in cn else ('Back' if 'Back' in cn and cn != 'WalkBack' else 'Belt')
        if cn == 'Idle': mode = 'Shoulder' if view == 'q' else 'Back'
        stealth = cn.endswith('Stealth')
        ln, sw, st = CARRY_STEALTH[mode] if stealth else CARRY[mode]
        if stealth: gun_vis = sw is not None and ((cn.startswith('Draw') and fr >= sw) or (cn.startswith('Holster') and fr < ln - sw))
        elif cn.startswith('Hide'): gun_vis = mode != 'Belt'
        else: gun_vis = cn in ('Reload', 'ArmedIdle', 'ArmedWalk') or (cn.startswith('Draw') and fr >= sw) or (cn.startswith('Holster') and fr < ln - sw)
        jo = jacket_open_at(st, fr if cn.startswith('Draw') else ln - fr) if cn.startswith(('Draw', 'Holster')) else (0.0, 0.0)
        under = (mode == 'Shoulder' and jo[0] > 0.2) or (mode == 'Back' and jo[1] > 0.2) or mode == 'Belt'
        set_back_gather(jo[1] if mode == 'Back' else 0.0); upd()
        vis('Pistol_Hand', gun_vis); vis('BotPistolL', False)
        for m_, names in (('Belt', ('Pistol_Holster', 'Holster')), ('Shoulder', ('Pistol_Shoulder', 'Holster_Shoulder')), ('Back', ('Pistol_Back',))):
            for nm in names: vis(nm, m_ == mode and not gun_vis and under)
        vis('Holster', mode == 'Belt'); vis('Holster_Shoulder', mode == 'Shoulder' and under)
        vis('Cig_Pack', cn == 'SmokeStart' and 16 <= fr < 58); vis('Lighter', cn == 'SmokeStart' and 64 <= fr < 116)
        vis('HandCig_', cn == 'SmokeLoop' or (cn == 'SmokeThrow' and fr < 12) or (cn == 'MouthThrow' and 9 <= fr < 16) or (cn == 'CigToMouth' and fr < 10))
        mouth_cig = (cn == 'SmokeStart' and fr >= 38) or (cn == 'MouthThrow' and fr < 9) or (cn == 'CigToMouth' and fr >= 10)
        rl = cn == 'Reload'
        vis('Mag_InGun', gun_vis and not (rl and 18 <= fr < INSERT_FRAME)); vis('Mag_InHand', rl and 34 <= fr < INSERT_FRAME)
        hide_head = view == 'e'
        for o in CHAR.objects:
            if o.name.startswith(('Head_Mesh', 'Hair_', 'Ear_', 'Sunglasses', 'Eyebrows', 'Neck_Mesh', 'Shirt_Collar', 'Jacket_Collar', 'Tie_Knot')): o.hide_render = hide_head
            if o.name.startswith('Cig_') and not o.name.startswith('Cig_Pack'): o.hide_render = hide_head or not mouth_cig
        cd.lens = 50
        if view == 's': cam.location = V(-3.6, -0.6, 1.0); tgt = V(0, -0.1, 0.95)
        elif view == 'f': cam.location = V(1.2, -3.4, 1.2); tgt = V(0, 0, 0.95)
        elif view == 'q': cam.location = V(1.3, -1.6, 1.75); tgt = V(0.0, -0.15, 1.35)
        elif view == 'h': cam.location = V(-1.1, -0.85, 1.25); tgt = V(-0.2, 0.0, 1.08)
        elif view == 'b': cam.location = V(-1.0, 1.0, 1.4); tgt = V(-0.2, 0.0, 1.1)
        elif view == 'B': cam.location = V(1.0, 1.35, 1.45); tgt = V(0.0, 0.12, 1.05)
        elif view == 'S': cam.location = V(1.55, 0.75, 1.3); tgt = V(0.0, 0.14, 1.05)
        elif view == 'K': cam.location = V(0.35, 1.45, 1.3); tgt = V(0.0, 0.1, 1.08)
        elif view == 'X': cam.location = V(1.9, 0.12, 1.15); tgt = V(0.0, 0.12, 1.1)
        elif view == 'L': cam.location = V(-3.0, -0.4, 1.7); tgt = V(0.0, 0.45, 0.35)
        else: cam.location = eye; tgt = eye + V(0, -1, -0.25); cd.lens = 34.6   # first-person view (vertical FOV 55, looking down 14 deg)
        cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
        path = os.path.join(OUT, '_prev_%02d.png' % i); scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(path); w, h = im.size
        tiles.append(np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4))
        bpy.data.images.remove(im); os.remove(path)
    rows = [np.concatenate(tiles[r * 8:(r + 1) * 8], axis=1) for r in range(len(tiles) // 8)]
    sheet = np.concatenate(rows[::-1], axis=0); h, w = sheet.shape[:2]
    img = bpy.data.images.new('sheet', w, h); img.pixels = sheet.ravel()
    img.filepath_raw = os.path.join(OUT, 'anim_preview.png'); img.file_format = 'PNG'; img.save()
    set_back_gather(0.0)
    print('PREVIEW SAVED')
    for o in CHAR.objects: o.hide_render = False
    bpy.data.objects.remove(cam)

# ------------------------------------------------------------------ save + export
rig.animation_data.action = bpy.data.actions['Idle']
reset_pose(); scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'Vince_Slim_Walker_Animated.blend'))

# ------------------------------------------------------------------ game-ready meshes (the .blend above keeps every part separate)
# ~65 skinned parts per character were ~65 draw calls / skinning jobs per person in Unity (19 people in the bar).
# Parts that always show together are merged (materials stay as sub-meshes), the rest keep their own object because the
# game toggles them (pistols, holsters, cigarettes, magazine). Body / head get two decimated LODs for the bots.
# A second UV set "FabricUV" maps cloth in metres along the body / limbs (seam at the back), so suit patterns
# (pinstripe, herringbone ...) run down the torso, the legs and along the sleeves.
MERGE = {
    'Body': ['Jacket_Body', 'Jacket_Sleeve_R', 'Jacket_Sleeve_L', 'Jacket_Lapels', 'Jacket_PocketFlaps', 'Jacket_Buttons',
             'Shirt', 'Shirt_Cuff_R', 'Shirt_Cuff_L', 'Pants_Leg_R', 'Pants_Leg_L', 'Pants_Pelvis', 'Belt', 'Belt_Buckle', 'Belt_Tail',
             'Shoe_Upper_R', 'Shoe_Upper_L', 'Shoe_Sole_R', 'Shoe_Sole_L', 'Shoe_Laces_R', 'Shoe_Laces_L', 'Hand_R', 'Hand_L'],
    'HeadGroup': ['Head_Mesh', 'Hair_Strands', 'Hair_Cap', 'Ear_R', 'Ear_L', 'Neck_Mesh', 'Eyebrows', 'Shirt_Collar', 'Jacket_Collar'],
    'Tie': ['Tie_Blade', 'Tie_Knot'],
    'Sunglasses': ['Sunglasses_Frame', 'Sunglasses_Lenses'],
    'Pistol_Hand': ['Pistol_Hand', 'Pistol_Hand_Slide', 'Pistol_Hand_Grips'],
    'Pistol_Holster': ['Pistol_Holster', 'Pistol_Holster_Grips'],
    'Pistol_Shoulder': ['Pistol_Shoulder', 'Pistol_Shoulder_Grips'],
    'Pistol_Back': ['Pistol_Back', 'Pistol_Back_Grips'],
    'BotPistolL': ['BotPistolL', 'BotPistolL_Slide', 'BotPistolL_Grips'],
}
LODS = {'Body': (0.38, 0.13), 'HeadGroup': (0.32, 0.11)}
TORSO_PARTS = {'Jacket_Body', 'Jacket_Lapels', 'Jacket_PocketFlaps', 'Jacket_Buttons', 'Jacket_Collar', 'Shirt', 'Shirt_Collar',
               'Pants_Pelvis', 'Belt', 'Belt_Buckle', 'Belt_Tail', 'Tie_Blade', 'Tie_Knot'}
SLEEVES = {'Jacket_Sleeve_R': -1, 'Jacket_Sleeve_L': 1, 'Shirt_Cuff_R': -1, 'Shirt_Cuff_L': 1}
LEGS = {'Pants_Leg_R': -1, 'Pants_Leg_L': 1}

def fabric_uv(name, p):
    """(u, v) in metres for a rest-pose world position."""
    if name in SLEEVES:
        S, D, Uv = arm_frame(SLEEVES[name])
        d = p - S; along = d.dot(D); perp = d - D * along
        Bv = D.cross(Uv)
        return 0.05 * math.atan2(perp.dot(Bv), perp.dot(Uv)), -along
    if name in LEGS:
        x0 = LEGS[name] * 0.095
        return 0.08 * math.atan2(p.x - x0, -p.y), p.z
    if name in TORSO_PARTS:
        return 0.17 * math.atan2(p.x, -p.y), p.z
    return p.x, p.z

def bake_modifiers(o):
    """Apply every modifier except the armature (rest pose), keep the vertex groups."""
    for m in o.modifiers:
        if m.type == 'ARMATURE': m.show_viewport = False
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    old = o.data; o.data = me
    for m in list(o.modifiers): o.modifiers.remove(m)
    if old.users == 0: bpy.data.meshes.remove(old)

def ensure_uvs(o, part_name):
    me = o.data
    if len(me.uv_layers) == 0: me.uv_layers.new(name='UVMap')
    me.uv_layers[0].name = 'UVMap'
    while len(me.uv_layers) > 1: me.uv_layers.remove(me.uv_layers[-1])
    fl = me.uv_layers.new(name='FabricUV')
    mw = o.matrix_world
    wrap = None
    if part_name in SLEEVES: wrap = 0.05 * math.pi
    elif part_name in LEGS: wrap = 0.08 * math.pi
    elif part_name in TORSO_PARTS: wrap = 0.17 * math.pi
    for poly in me.polygons:
        uvs = [fabric_uv(part_name, mw @ me.vertices[me.loops[li].vertex_index].co) for li in poly.loop_indices]
        if wrap and max(u for u, _ in uvs) - min(u for u, _ in uvs) > wrap:   # face across the back seam
            uvs = [(u + 2 * wrap if u < 0 else u, v) for u, v in uvs]
        for li, uv in zip(poly.loop_indices, uvs): fl.data[li].uv = uv

def join_group(target, names):
    objs = [bpy.data.objects.get(n) for n in names]
    objs = [o for o in objs if o and o.type == 'MESH']
    if not objs: return None
    for o in objs:
        bake_modifiers(o)
        ensure_uvs(o, o.name)
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    j = bpy.context.view_layer.objects.active
    j.name = target; j.data.name = target
    armature_mod(j)
    return j

def make_lod(src, suffix, ratio):
    d = src.copy(); d.data = src.data.copy(); d.name = src.name + suffix; d.data.name = d.name
    for c in src.users_collection: c.objects.link(d)
    for m in list(d.modifiers): d.modifiers.remove(m)
    dm = d.modifiers.new('Decimate', 'DECIMATE'); dm.ratio = ratio; dm.use_collapse_triangulate = True
    bake_modifiers(d)
    armature_mod(d)
    return d

rig.animation_data.action = None; reset_pose(); set_back_gather(0.0); upd()
merged = {}
for target, names in MERGE.items():
    j = join_group(target, names)
    if j: merged[target] = j
for target, (r1, r2) in LODS.items():
    if target in merged:
        make_lod(merged[target], '_LOD1', r1); make_lod(merged[target], '_LOD2', r2)
upd()
dgx = bpy.context.evaluated_depsgraph_get()
for o in CHAR.all_objects:
    if o.type == 'MESH':
        print('GAME MESH %-22s tris=%6d mats=%d uvs=%s' % (o.name, sum(len(p.vertices) - 2 for p in o.data.polygons), len(o.data.materials), [u.name for u in o.data.uv_layers]))

def export(path, objs, all_actions=True):
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'ARMATURE'},
                             use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=True,
                             bake_anim_use_all_actions=all_actions, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                             mesh_smooth_type='FACE', apply_scale_options='FBX_SCALE_ALL', armature_nodetype='NULL')

rig.animation_data.action = None; reset_pose()
export(os.path.join(OUT, 'Vince_Slim_Walker_Animated.fbx'), [o for o in CHAR.all_objects if o.type in ('MESH', 'ARMATURE')])
print('EXPORTED')

# ------------------------------------------------------------------ optional: a Mixamo-style test clip (renamed skeleton, no mesh)
if MIXAMO_TEST:
    MAP = {'Hips': 'Hips', 'Spine': 'Spine', 'Chest': 'Spine1', 'Neck': 'Neck', 'Head': 'Head'}
    for s, side in (('L', 'Left'), ('R', 'Right')):
        MAP.update({'Shoulder.' + s: side + 'Shoulder', 'UpperArm.' + s: side + 'Arm', 'ForeArm.' + s: side + 'ForeArm',
                    'Hand.' + s: side + 'Hand', 'Thigh.' + s: side + 'UpLeg', 'Shin.' + s: side + 'Leg',
                    'Foot.' + s: side + 'Foot', 'Toe.' + s: side + 'ToeBase'})
        for fn, mf in (('Thumb', 'Thumb'), ('Index', 'Index'), ('Middle', 'Middle'), ('Ring', 'Ring'), ('Pinky', 'Pinky')):
            for k in (1, 2, 3): MAP['%s%d.%s' % (fn, k, s)] = '%sHand%s%d' % (side, mf, k)
    rig.animation_data.action = bpy.data.actions['Run']   # must be assigned so the rename updates its F-Curves
    for b in rig.data.bones:
        if b.name in MAP: b.name = 'mixamorig:' + MAP[b.name]
    act = bpy.data.actions['Run']
    rig.animation_data.action = act
    try:
        if getattr(rig.animation_data, 'action_slot', None) is None and len(act.slots):
            rig.animation_data.action_slot = act.slots[0]
    except Exception as e:
        print('slot assign', e)
    scene.frame_start, scene.frame_end = 0, 22
    scene.frame_set(0); qa = rig.pose.bones['mixamorig:LeftUpLeg'].matrix.to_quaternion()
    scene.frame_set(6); qb = rig.pose.bones['mixamorig:LeftUpLeg'].matrix.to_quaternion()
    print('MIXAMO TEST thigh motion deg', math.degrees(qa.rotation_difference(qb).angle))
    export(os.path.join(OUT, 'MixamoTest_WalkBack.fbx'), [rig], all_actions=False)
    print('MIXAMO TEST EXPORTED')
