# Vince "Slim" Walker - procedural character build for Blender 5.x
import bpy, bmesh, math, os, sys, random
from math import sin, cos, pi, radians, sqrt, exp, acos, atan2
from mathutils import Vector, Matrix, noise
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else os.getcwd()
random.seed(11)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

def V(*a): return Vector(a)
XA = V(1, 0, 0); YA = V(0, 1, 0); ZA = V(0, 0, 1)

def ss(e0, e1, x):
    if e0 == e1: return 0.0 if x < e0 else 1.0
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t)
def lerp(a, b, t): return a + (b - a) * t
def clamp(x, a, b): return max(a, min(b, x))
def interp(keys, x):
    if x <= keys[0][0]: return keys[0][1:]
    for i in range(len(keys) - 1):
        k0, k1 = keys[i], keys[i + 1]
        if x <= k1[0]:
            t = (x - k0[0]) / (k1[0] - k0[0])
            return tuple(lerp(a, b, t) for a, b in zip(k0[1:], k1[1:]))
    return keys[-1][1:]
def sgnpow(c, p): return math.copysign(abs(c) ** p, c)
def nz(p, s=1.0): return noise.noise(p * s, noise_basis='PERLIN_ORIGINAL')
def g2(dx, sx, dz, sz): return exp(-(dx * dx) / (sx * sx) - (dz * dz) / (sz * sz))

def mkcol(name, parent=None):
    c = bpy.data.collections.new(name); (parent or scene.collection).children.link(c); return c
CHAR = mkcol('Vince_Slim_Walker')
ENV = mkcol('Env_Stage')
ALLEY = mkcol('Env_Alley')
LIGHTS = mkcol('Lights')

# ------------------------------------------------------------------ mesh builder
class MB:
    def __init__(s): s.v = []; s.f = []
    def add_rings(s, rings, closed=True, cap0=False, cap1=False, c0=None, c1=None):
        n = len(rings[0]); base = len(s.v); m = len(rings)
        for r in rings: s.v.extend([Vector(p) for p in r])
        rng = n if closed else n - 1
        for i in range(m - 1):
            for j in range(rng):
                j2 = (j + 1) % n
                s.f.append((base + i * n + j, base + i * n + j2, base + (i + 1) * n + j2, base + (i + 1) * n + j))
        if cap0:
            c = c0 if c0 is not None else sum(rings[0], Vector((0, 0, 0))) / n
            ci = len(s.v); s.v.append(Vector(c))
            for j in range(n): s.f.append((ci, base + (j + 1) % n, base + j))
        if cap1:
            c = c1 if c1 is not None else sum(rings[-1], Vector((0, 0, 0))) / n
            ci = len(s.v); s.v.append(Vector(c)); b = base + (m - 1) * n
            for j in range(n): s.f.append((ci, b + j, b + (j + 1) % n))
    def grid(s, rows):
        s.add_rings(rows, closed=False)
    def obj(s, name, mat=None, col=None, smooth=True, recalc=True):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in s.v], [], s.f)
        me.validate()
        if recalc:
            bm = bmesh.new(); bm.from_mesh(me)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            bm.to_mesh(me); bm.free()
        me.polygons.foreach_set('use_smooth', [smooth] * len(me.polygons))
        me.update()
        ob = bpy.data.objects.new(name, me); (col or CHAR).objects.link(ob)
        if mat:
            for m in (mat if isinstance(mat, (list, tuple)) else [mat]): me.materials.append(m)
        return ob

def ring(c, u, v, a, b, n, e=2.0, t0=0.0, t1=2 * pi, closed=True, mod=None):
    pts = []
    for k in range(n):
        t = t0 + (t1 - t0) * k / (n if closed else (n - 1))
        x = a * sgnpow(cos(t), 2.0 / e); y = b * sgnpow(sin(t), 2.0 / e)
        p = c + u * x + v * y
        if mod: p = mod(p, t, x / a if a else 0.0, y / b if b else 0.0)
        pts.append(p)
    return pts

def rmf(pts, up):
    n = len(pts); T = []
    for i in range(n):
        if i == 0: t = pts[1] - pts[0]
        elif i == n - 1: t = pts[-1] - pts[-2]
        else: t = pts[i + 1] - pts[i - 1]
        T.append(t.normalized())
    nn = up - T[0] * up.dot(T[0])
    if nn.length < 1e-6: nn = V(1, 0, 0) - T[0] * T[0].x
    nn.normalize(); N = [nn]
    for i in range(1, n):
        q = T[i - 1].rotation_difference(T[i]); nn = q @ N[-1]
        nn = (nn - T[i] * nn.dot(T[i])).normalized(); N.append(nn)
    B = [T[i].cross(N[i]) for i in range(n)]
    return T, N, B

def tube(mb, pts, rads, n=10, up=ZA, rads2=None, caps=(True, True), rcap=(True, True), e=2.0, mod=None):
    pts = [Vector(p) for p in pts]
    rads2 = rads2 or rads
    T, N, B = rmf(pts, up)
    rings = []
    def rg(c, i, r1, r2):
        return ring(c, N[i], B[i], r1, r2, n, e=e, mod=mod)
    if rcap[0]:
        for ph in (70, 45, 20):
            ph = radians(ph)
            rings.append(rg(pts[0] - T[0] * rads[0] * sin(ph), 0, rads[0] * cos(ph), rads2[0] * cos(ph)))
    for i, p in enumerate(pts): rings.append(rg(p, i, rads[i], rads2[i]))
    if rcap[1]:
        for ph in (20, 45, 70):
            ph = radians(ph)
            rings.append(rg(pts[-1] + T[-1] * rads[-1] * sin(ph), -1, rads[-1] * cos(ph), rads2[-1] * cos(ph)))
    c0 = pts[0] - T[0] * rads[0] if rcap[0] else None
    c1 = pts[-1] + T[-1] * rads[-1] if rcap[1] else None
    mb.add_rings(rings, cap0=caps[0], cap1=caps[1], c0=c0, c1=c1)

def tube_loop(mb, pts, rads, nrm, n=8, rads2=None):
    m = len(pts); rads2 = rads2 or rads; rings = []
    for i in range(m):
        t = (pts[(i + 1) % m] - pts[i - 1]).normalized()
        nn = (nrm - t * nrm.dot(t)).normalized(); b = t.cross(nn)
        rings.append(ring(pts[i], nn, b, rads[i], rads2[i], n))
    rings.append(rings[0])
    mb.add_rings(rings)

def ellipsoid(mb, c, ax, ay, az, rx, ry, rz, nu=12, nv=8):
    rings = []
    for i in range(1, nv):
        ph = -pi / 2 + pi * i / nv
        rings.append([c + ax * (rx * cos(ph) * cos(t)) + ay * (ry * cos(ph) * sin(t)) + az * (rz * sin(ph))
                      for t in [2 * pi * k / nu for k in range(nu)]])
    mb.add_rings(rings, cap0=True, cap1=True, c0=c - az * rz, c1=c + az * rz)

def box(mb, c, ax, ay, az, hx, hy, hz):
    b = len(mb.v)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                mb.v.append(c + ax * (sx * hx) + ay * (sy * hy) + az * (sz * hz))
    idx = lambda sx, sy, sz: b + ((sx > 0) * 4 + (sy > 0) * 2 + (sz > 0))
    F = [((-1, -1, -1), (-1, 1, -1), (-1, 1, 1), (-1, -1, 1)), ((1, -1, -1), (1, -1, 1), (1, 1, 1), (1, 1, -1)),
         ((-1, -1, -1), (-1, -1, 1), (1, -1, 1), (1, -1, -1)), ((-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)),
         ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1)), ((-1, -1, 1), (-1, 1, 1), (1, 1, 1), (1, -1, 1))]
    for f in F: mb.f.append(tuple(idx(*k) for k in f))

def cylinder(mb, p0, p1, r, n=12, r1=None):
    d = (p1 - p0); T = d.normalized()
    u = (ZA if abs(T.z) < 0.9 else XA); u = (u - T * u.dot(T)).normalized(); v = T.cross(u)
    r1 = r if r1 is None else r1
    rings = [ring(p0, u, v, r, r, n), ring(p1, u, v, r1, r1, n)]
    mb.add_rings(rings, cap0=True, cap1=True)

def resample(pts, k):
    pts = [Vector(p) for p in pts]
    d = [0.0]
    for i in range(1, len(pts)): d.append(d[-1] + (pts[i] - pts[i - 1]).length)
    out = []
    for j in range(k):
        s = d[-1] * j / (k - 1)
        for i in range(1, len(pts)):
            if s <= d[i] + 1e-9:
                t = (s - d[i - 1]) / max(1e-9, d[i] - d[i - 1]); out.append(pts[i - 1].lerp(pts[i], t)); break
    return out

def add_mod(ob, kind, name=None, **kw):
    m = ob.modifiers.new(name or kind, kind)
    for k, v in kw.items(): setattr(m, k, v)
    return m

def subsurf(ob, lv=1, rl=None):
    return add_mod(ob, 'SUBSURF', 'Subdivision', levels=lv, render_levels=rl if rl is not None else lv)

def apply_mods(ob):
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    old = ob.data; ob.modifiers.clear(); ob.data = me
    bpy.data.meshes.remove(old)

def smooth_all(ob):
    me = ob.data; me.polygons.foreach_set('use_smooth', [True] * len(me.polygons)); me.update()

# ------------------------------------------------------------------ materials
def new_mat(name):
    m = bpy.data.materials.new(name)
    try: m.use_nodes = True
    except Exception: pass
    nt = m.node_tree
    b = nt.nodes.get('Principled BSDF')
    if b is None:
        b = nt.nodes.new('ShaderNodeBsdfPrincipled')
        out = nt.nodes.get('Material Output') or nt.nodes.new('ShaderNodeOutputMaterial')
        nt.links.new(b.outputs[0], out.inputs['Surface'])
    return m, nt, b

def set_in(node, name, val):
    if name in node.inputs: node.inputs[name].default_value = val

def mix_rgb(nt, fac, a, b):
    mx = nt.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'
    if isinstance(fac, float): mx.inputs[0].default_value = fac
    else: nt.links.new(fac, mx.inputs[0])
    for idx, val in ((6, a), (7, b)):
        if isinstance(val, tuple): mx.inputs[idx].default_value = (*val, 1)
        else: nt.links.new(val, mx.inputs[idx])
    return mx.outputs[2]

def noise_fac(nt, coord, scale, detail=6, lo=0.4, hi=0.7, rough=0.55, stretch=None):
    N = nt.nodes
    src = coord
    if stretch:
        mp = N.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = stretch
        nt.links.new(coord, mp.inputs['Vector']); src = mp.outputs['Vector']
    n = N.new('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale
    n.inputs['Detail'].default_value = detail; set_in(n, 'Roughness', rough)
    nt.links.new(src, n.inputs['Vector'])
    r = N.new('ShaderNodeMapRange'); r.inputs['From Min'].default_value = lo; r.inputs['From Max'].default_value = hi
    nt.links.new(n.outputs['Fac'], r.inputs['Value'])
    return r.outputs['Result']

def pbr(name, base, rough=0.5, metal=0.0, sheen=0.0, sheen_tint=(0.5, 0.5, 0.5), coat=0.0, coat_rough=0.1,
        sss=0.0, var=None, bumps=(), emit=None, emit_str=0.0, rough_var=None, spec=0.5):
    m, nt, b = new_mat(name)
    N = nt.nodes
    set_in(b, 'Base Color', (*base, 1)); set_in(b, 'Roughness', rough); set_in(b, 'Metallic', metal)
    set_in(b, 'Specular IOR Level', spec)
    if sheen: set_in(b, 'Sheen Weight', sheen); set_in(b, 'Sheen Tint', (*sheen_tint, 1)); set_in(b, 'Sheen Roughness', 0.45)
    if coat: set_in(b, 'Coat Weight', coat); set_in(b, 'Coat Roughness', coat_rough)
    if sss: set_in(b, 'Subsurface Weight', sss); set_in(b, 'Subsurface Radius', (1.0, 0.35, 0.18)); set_in(b, 'Subsurface Scale', 0.012)
    if emit: set_in(b, 'Emission Color', (*emit, 1)); set_in(b, 'Emission Strength', emit_str)
    tc = N.new('ShaderNodeTexCoord'); co = tc.outputs['Object']
    col = (*base,)
    if var:  # list of (color, scale, lo, hi, stretch)
        cur = base
        for (c2, sc, lo, hi, st) in var:
            f = noise_fac(nt, co, sc, lo=lo, hi=hi, stretch=st)
            cur = mix_rgb(nt, f, cur, c2)
        nt.links.new(cur, b.inputs['Base Color'])
    if rough_var:
        lo_r, hi_r, sc = rough_var
        f = noise_fac(nt, co, sc, lo=0.3, hi=0.7)
        mr = N.new('ShaderNodeMapRange'); mr.inputs['To Min'].default_value = lo_r; mr.inputs['To Max'].default_value = hi_r
        nt.links.new(f, mr.inputs['Value']); nt.links.new(mr.outputs['Result'], b.inputs['Roughness'])
    prev = None
    for bp in bumps:  # (kind, scale, strength, distance, stretch)
        kind, sc, stg, dist = bp[:4]; st = bp[4] if len(bp) > 4 else None
        src = co
        if st:
            mp = N.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = st
            nt.links.new(co, mp.inputs['Vector']); src = mp.outputs['Vector']
        if kind == 'noise':
            t = N.new('ShaderNodeTexNoise'); t.inputs['Scale'].default_value = sc; t.inputs['Detail'].default_value = 8
            hgt = t.outputs['Fac']
        elif kind == 'voronoi':
            t = N.new('ShaderNodeTexVoronoi'); t.inputs['Scale'].default_value = sc; hgt = t.outputs['Distance']
        elif kind == 'wave':
            t = N.new('ShaderNodeTexWave'); t.inputs['Scale'].default_value = sc; set_in(t, 'Distortion', 2.0); hgt = t.outputs['Fac']
        nt.links.new(src, t.inputs['Vector'])
        bn = N.new('ShaderNodeBump'); bn.inputs['Strength'].default_value = stg; bn.inputs['Distance'].default_value = dist
        nt.links.new(hgt, bn.inputs['Height'])
        if prev: nt.links.new(prev, bn.inputs['Normal'])
        prev = bn.outputs['Normal']
    if prev: nt.links.new(prev, b.inputs['Normal'])
    return m

def emission_mat(name, color, strength):
    m, nt, b = new_mat(name)
    set_in(b, 'Base Color', (0, 0, 0, 1)); set_in(b, 'Emission Color', (*color, 1)); set_in(b, 'Emission Strength', strength)
    set_in(b, 'Roughness', 0.3)
    return m

MAT = {}
MAT['suit'] = pbr('Suit_Wool', (0.022, 0.022, 0.025), rough=0.8, sheen=0.25, sheen_tint=(0.5, 0.5, 0.6),
                  var=[((0.034, 0.032, 0.031), 2.2, 0.6, 0.9, None), ((0.012, 0.012, 0.014), 7.0, 0.5, 0.8, None)],
                  bumps=[('noise', 900, 0.10, 0.002), ('noise', 22, 0.12, 0.01, (1, 1, 3.5))], rough_var=(0.7, 0.9, 5))
MAT['shirt'] = pbr('Shirt_Cotton', (0.80, 0.78, 0.72), rough=0.62, sheen=0.4,
                   var=[((0.62, 0.58, 0.50), 4.0, 0.55, 0.9, None)], bumps=[('noise', 1200, 0.1, 0.002), ('noise', 30, 0.25, 0.006, (1, 1, 3))])
MAT['tie'] = pbr('Tie_Silk', (0.010, 0.010, 0.012), rough=0.32, sheen=0.5, coat=0.15, coat_rough=0.3,
                 bumps=[('wave', 160, 0.06, 0.002)])
MAT['skin'] = None  # built below with stubble
MAT['leather'] = pbr('Leather_Brown', (0.16, 0.075, 0.035), rough=0.42, coat=0.25, coat_rough=0.25,
                     var=[((0.07, 0.035, 0.018), 6, 0.5, 0.85, None)], bumps=[('voronoi', 350, 0.18, 0.002), ('noise', 40, 0.15, 0.004)],
                     rough_var=(0.3, 0.6, 9))
MAT['shoe'] = pbr('Shoe_Leather', (0.060, 0.030, 0.013), rough=0.4, coat=0.3, coat_rough=0.2,
                  var=[((0.045, 0.022, 0.012), 5, 0.45, 0.8, None), ((0.25, 0.16, 0.10), 12, 0.7, 0.95, None)],
                  bumps=[('voronoi', 420, 0.12, 0.0015), ('noise', 35, 0.2, 0.004, (1, 1, 2))], rough_var=(0.2, 0.55, 8))
MAT['sole'] = pbr('Sole_Rubber', (0.025, 0.02, 0.018), rough=0.75, bumps=[('noise', 300, 0.2, 0.002)])
MAT['metal'] = pbr('Brass_Old', (0.55, 0.47, 0.32), rough=0.32, metal=1.0, var=[((0.2, 0.17, 0.12), 25, 0.5, 0.8, None)])
MAT['gunmetal'] = pbr('Gun_Blued', (0.045, 0.047, 0.055), rough=0.28, metal=1.0,
                      var=[((0.16, 0.16, 0.17), 30, 0.65, 0.9, None)], rough_var=(0.2, 0.45, 20))
MAT['wood'] = pbr('Grip_Walnut', (0.20, 0.085, 0.035), rough=0.38, coat=0.4,
                  var=[((0.08, 0.03, 0.012), 18, 0.4, 0.7, (1, 1, 6))], bumps=[('voronoi', 600, 0.4, 0.001)])
MAT['holster'] = pbr('Holster_Leather', (0.10, 0.05, 0.022), rough=0.5, coat=0.15,
                     var=[((0.09, 0.045, 0.02), 5, 0.5, 0.85, None)], bumps=[('voronoi', 300, 0.2, 0.002)])
MAT['hair'] = pbr('Hair_Pomade', (0.008, 0.007, 0.007), rough=0.38, coat=0.2, coat_rough=0.25,
                  var=[((0.03, 0.026, 0.024), 60, 0.5, 0.9, (1, 1, 0.15))], bumps=[('noise', 220, 0.5, 0.003, (1, 4, 1))])
MAT['lens'] = pbr('Lens_Dark', (0.004, 0.004, 0.006), rough=0.04, coat=1.0, coat_rough=0.02, spec=1.0)
MAT['frame'] = pbr('Frame_Acetate', (0.008, 0.008, 0.009), rough=0.18, coat=0.6, coat_rough=0.1)
MAT['paper'] = pbr('Cig_Paper', (0.86, 0.85, 0.80), rough=0.65, sss=0.2)
MAT['filter'] = pbr('Cig_Filter', (0.78, 0.48, 0.22), rough=0.6, var=[((0.55, 0.30, 0.12), 400, 0.5, 0.8, None)])
MAT['ember'] = pbr('Cig_Ember', (0.2, 0.05, 0.01), rough=0.9, emit=(1.0, 0.32, 0.06), emit_str=25.0)
MAT['ash'] = pbr('Cig_Ash', (0.32, 0.31, 0.30), rough=0.95, bumps=[('noise', 800, 0.6, 0.001)])
MAT['button'] = pbr('Button_Horn', (0.03, 0.022, 0.018), rough=0.25, coat=0.5)

def skin_mat():
    m, nt, b = new_mat('Skin')
    N = nt.nodes; L = nt.links
    set_in(b, 'Roughness', 0.48); set_in(b, 'Subsurface Weight', 0.18)
    set_in(b, 'Subsurface Radius', (1.0, 0.35, 0.18)); set_in(b, 'Subsurface Scale', 0.012); set_in(b, 'Specular IOR Level', 0.45)
    tc = N.new('ShaderNodeTexCoord'); co = tc.outputs['Object']
    base = (0.44, 0.28, 0.20)
    f = noise_fac(nt, co, 30, lo=0.45, hi=0.75)
    c1 = mix_rgb(nt, f, base, (0.66, 0.40, 0.33))  # mottling / redness
    at = N.new('ShaderNodeAttribute'); at.attribute_name = 'stubble'
    dots = noise_fac(nt, co, 900, detail=2, lo=0.35, hi=0.65)
    mm = N.new('ShaderNodeMath'); mm.operation = 'MULTIPLY'
    L.new(at.outputs['Fac'], mm.inputs[0]); L.new(dots, mm.inputs[1])
    mm2 = N.new('ShaderNodeMath'); mm2.operation = 'MULTIPLY_ADD'
    L.new(at.outputs['Fac'], mm2.inputs[0]); mm2.inputs[1].default_value = 0.22; L.new(mm.outputs[0], mm2.inputs[2])
    c2 = mix_rgb(nt, mm2.outputs[0], c1, (0.20, 0.15, 0.13))
    fmn = N.new('ShaderNodeAttribute'); fmn.attribute_name = 'facemask'
    sep = N.new('ShaderNodeSeparateColor'); L.new(fmn.outputs['Color'], sep.inputs[0])
    lipm = N.new('ShaderNodeMath'); lipm.operation = 'MULTIPLY'; L.new(sep.outputs[1], lipm.inputs[0]); lipm.inputs[1].default_value = 0.55
    c3 = mix_rgb(nt, lipm.outputs[0], c2, (0.36, 0.17, 0.15))
    cvm = N.new('ShaderNodeMath'); cvm.operation = 'MULTIPLY'; L.new(sep.outputs[0], cvm.inputs[0]); cvm.inputs[1].default_value = 0.75
    c4 = mix_rgb(nt, cvm.outputs[0], c3, (0.08, 0.04, 0.035))
    L.new(c4, b.inputs['Base Color'])
    t = N.new('ShaderNodeTexNoise'); t.inputs['Scale'].default_value = 1500; t.inputs['Detail'].default_value = 4
    L.new(co, t.inputs['Vector'])
    bn = N.new('ShaderNodeBump'); bn.inputs['Strength'].default_value = 0.08; bn.inputs['Distance'].default_value = 0.001
    L.new(t.outputs['Fac'], bn.inputs['Height'])
    t2 = N.new('ShaderNodeTexNoise'); t2.inputs['Scale'].default_value = 120; L.new(co, t2.inputs['Vector'])
    bn2 = N.new('ShaderNodeBump'); bn2.inputs['Strength'].default_value = 0.12; bn2.inputs['Distance'].default_value = 0.002
    L.new(t2.outputs['Fac'], bn2.inputs['Height']); L.new(bn.outputs['Normal'], bn2.inputs['Normal'])
    L.new(bn2.outputs['Normal'], b.inputs['Normal'])
    return m
MAT['skin'] = skin_mat()

# ================================================================== BODY LAYOUT
DR = radians(8)
def arm_frame(sgn):
    S = V(sgn * 0.19, 0.006, 1.455)
    D = V(sgn * cos(DR), 0, -sin(DR))
    U = V(sgn * sin(DR), 0, cos(DR))
    return S, D, U

# ------------------------------------------------------------------ HEAD
HC = V(0, 0.006, 1.708)

def head_local(p):
    x, y, z = p.x, p.y, p.z
    if y < 0:
        rh = sqrt(x * x + y * y)
        if rh > 1e-6:
            ph = atan2(y, x)
            e = 2.0 + 0.75 * ss(-1.0, -0.35, z) * ss(0.95, 0.45, z)
            x, y = rh * sgnpow(cos(ph), 2 / e), rh * sgnpow(sin(ph), 2 / e)
    X = 0.071 * x; Y = 0.093 * y; Z = 0.113 * z
    if y > 0: Y *= 1 + 0.07 * ss(-0.4, 0.5, z)
    jaw = ss(0.05, -0.95, z)
    X *= (1 + 0.16 * ss(0.0, -0.6, z)) * (1 - 0.34 * ss(-0.5, -1.0, z))
    if y > 0: Y *= 1 - 0.5 * ss(-0.2, -0.95, z)
    Y -= 0.018 * ss(-0.25, -1.0, z)
    if y < 0 and z > 0: Y *= 1 - 0.05 * ss(0.2, 0.9, z)
    if y < 0: X *= 1 + 0.06 * ss(0.0, -0.7, y) * (1 - jaw)
    if y < 0: Y *= 1 + 0.10 * ss(0.0, 0.5, z) * ss(0.97, 0.6, z)
    # jaw-line base plane: head underside slopes up from chin to the back of the jaw
    zj = lerp(-0.046, -0.110, ss(0.035, -0.072, Y))
    k = 0.007
    hh = clamp(0.5 + 0.5 * (Z - zj) / k, 0, 1)
    Z = lerp(zj, Z, hh) + k * hh * (1 - hh)
    return Vector((X, Y, Z))

def seg_dist(px, pz, x0, z0, x1, z1):
    dx, dz = x1 - x0, z1 - z0
    t = clamp(((px - x0) * dx + (pz - z0) * dz) / (dx * dx + dz * dz), 0, 1)
    return sqrt((px - x0 - t * dx) ** 2 + (pz - z0 - t * dz) ** 2)

def mouth_z(X): return -0.065 + 0.0030 * ss(0.0, -0.022, X)

def face_disp(X, Y, Z):
    d = 0.0; n = 0.0; ax = abs(X)
    if -0.06 < Z < 0.045:
        t = clamp((0.022 - Z) / (0.022 + 0.036), 0, 1)
        h = (0.005 + 0.028 * t ** 1.4) * ss(0.042, 0.018, Z) * ss(-0.053, -0.038, Z)
        sw = 0.0075 + 0.0060 * t
        d += h * exp(-(X / sw) ** 2)
    d += 0.005 * g2(X, 0.009, Z + 0.034, 0.008)
    d += 0.0095 * g2(ax - 0.016, 0.0068, Z + 0.039, 0.0065)
    d += 0.0085 * g2(ax - 0.030, 0.024, Z - 0.030, 0.009)
    d += 0.0030 * g2(X, 0.012, Z - 0.024, 0.010)
    d -= 0.0090 * g2(ax - 0.032, 0.015, Z - 0.010, 0.010)
    n += 0.0085 * g2(ax - 0.052, 0.018, Z + 0.012, 0.012)
    n -= 0.0050 * g2(ax - 0.047, 0.014, Z + 0.055, 0.016)
    n -= 0.0030 * g2(ax - 0.064, 0.012, Z - 0.032, 0.016)
    d += 0.0105 * g2(X, 0.030, Z + 0.064, 0.022)
    mz = mouth_z(X)
    d += 0.0032 * g2(X, 0.018, Z - (mz + 0.0042), 0.0035)
    d += 0.0042 * g2(X, 0.016, Z - (mz - 0.0058), 0.0042)
    d -= 0.0045 * exp(-(X / 0.021) ** 4) * exp(-((Z - mz) / 0.0018) ** 2)
    d -= 0.0020 * g2(X, 0.015, Z + 0.080, 0.004)
    d += 0.0095 * g2(X, 0.021, Z + 0.098, 0.013)
    dist = seg_dist(ax, Z, 0.020, -0.034, 0.031, -0.069)
    d -= 0.0018 * exp(-(dist / 0.0028) ** 2)
    d += 0.0010 * exp(-((dist - 0.006) / 0.004) ** 2) * ss(0.015, 0.03, ax)
    return d, n

def build_head():
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=80, v_segments=56, radius=1.0)
    stub = []; fmask = []
    for v in bm.verts:
        p = v.co.copy()
        L = head_local(p)
        nrm = Vector((L.x / 0.071 ** 2, L.y / 0.093 ** 2, L.z / 0.113 ** 2)).normalized()
        wf = ss(-0.02, -0.06, L.y)
        d, n = face_disp(L.x, L.y, L.z)
        ws = ss(-0.0, -0.05, L.y)
        L = L + V(0, -d * wf, 0) + nrm * (n * ws)
        v.co = HC + L
        # stubble mask: jaw, chin, upper lip, cheeks below cheekbone
        s = ss(-0.010, -0.040, L.z) * ss(0.06, -0.01, L.y) * (1 - ss(-0.105, -0.125, L.z))
        lips = exp(-(L.x / 0.022) ** 4) * exp(-((L.z - mouth_z(L.x)) / 0.0075) ** 2)
        s *= (1 - lips) * (1 - 0.6 * exp(-((L.z + 0.047) / 0.008) ** 2) * exp(-(L.x / 0.02) ** 2))
        stub.append(clamp(s, 0, 1))
        X_, Z_ = L.x, L.z; ax_ = abs(X_); mz_ = mouth_z(X_)
        cav = 0.9 * exp(-(X_ / 0.021) ** 4) * exp(-((Z_ - mz_) / 0.0022) ** 2)
        cav += 0.45 * exp(-(seg_dist(ax_, Z_, 0.020, -0.034, 0.031, -0.069) / 0.0035) ** 2)
        cav += 0.5 * g2(ax_ - 0.023, 0.004, Z_ + 0.039, 0.007) + 0.8 * g2(ax_ - 0.0085, 0.0045, Z_ + 0.046, 0.0028)
        cav += 0.35 * g2(X_, 0.015, Z_ + 0.080, 0.004)
        lip = exp(-(X_ / 0.019) ** 4) * exp(-((Z_ - mz_) / 0.0068) ** 2)
        fmask.append((clamp(cav * wf, 0, 1), clamp(lip * wf, 0, 1)))
    me = bpy.data.meshes.new('Head'); bm.to_mesh(me)
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    me.polygons.foreach_set('use_smooth', [True] * len(me.polygons))
    ca = me.color_attributes.new('stubble', 'FLOAT_COLOR', 'POINT')
    for i, s in enumerate(stub): ca.data[i].color = (s, s, s, 1)
    fm = me.color_attributes.new('facemask', 'FLOAT_COLOR', 'POINT')
    for i, (cv, lp) in enumerate(fmask): fm.data[i].color = (cv, lp, 0, 1)
    ob = bpy.data.objects.new('Head', me); CHAR.objects.link(ob); me.materials.append(MAT['skin'])
    return ob, bvh

head_ob, HBVH = build_head()

def hit_front(x, z, local=True):
    o = HC + V(x, -0.4, z) if local else V(x, -0.4, z)
    r = HBVH.ray_cast(o, V(0, 1, 0))
    return r[0], r[1]

def hit_side(sgn, y, z):
    r = HBVH.ray_cast(HC + V(sgn * 0.4, y, z), V(-sgn, 0, 0))
    return r[0], r[1]

# ------------------------------------------------------------------ NECK
def build_neck():
    mb = MB(); rings = []
    for i in range(9):
        t = i / 8; z = lerp(1.46, 1.675, t)
        c = V(0, lerp(0.016, 0.006, t), z)
        rx = 0.055 - 0.003 * ss(0.2, 0.6, t) + 0.004 * ss(0.8, 1.0, t)
        ry = 0.058 - 0.003 * ss(0.2, 0.6, t)
        def mod(p, a, cx, cy, z=z):
            bump = 0.004 * exp(-((z - 1.585) / 0.012) ** 2) * exp(-(cx / 0.25) ** 2) * (cy < 0) * abs(cy)
            return p + V(0, -bump, 0)
        rings.append(ring(c, XA, YA, rx, ry, 20, mod=mod))
    mb.add_rings(rings)
    ob = mb.obj('Neck', MAT['skin']); subsurf(ob, 1)
    return ob
build_neck()

# ------------------------------------------------------------------ EARS
def build_ear(sgn):
    E = HC + V(sgn * 0.066, 0.012, -0.002)
    ang = radians(18)
    H = V(sgn * sin(ang), cos(ang), 0)              # back along ear
    Vv = V(0, -sin(radians(10)), cos(radians(10)))  # up, tilted back
    Nn = H.cross(Vv); Nn = Nn if Nn.x * sgn > 0 else -Nn
    def P(h, v, o): h *= 0.88; v *= 0.9; return E + H * h + Vv * v + Nn * (o + 0.0 + 0.16 * h)
    mb = MB()
    helix = [(0.002, 0.010), (0.004, 0.023), (0.012, 0.031), (0.022, 0.028), (0.028, 0.016), (0.0275, 0.002),
             (0.023, -0.012), (0.017, -0.022)]
    hp = [P(h, v, 0.002) for h, v in helix]
    tube(mb, resample(hp, 14), [0.0040] * 14, n=8, up=Nn)
    ellipsoid(mb, P(0.014, -0.025, 0.0), H, Vv, Nn, 0.008, 0.009, 0.0045)
    ellipsoid(mb, P(0.013, 0.002, -0.003), H, Vv, Nn, 0.012, 0.021, 0.0045)
    anti = [P(h, v, 0.001) for h, v in [(0.011, 0.020), (0.018, 0.013), (0.019, -0.003), (0.014, -0.013)]]
    tube(mb, resample(anti, 8), [0.0024] * 8, n=6, up=Nn)
    ellipsoid(mb, P(0.004, -0.004, -0.008), H, Vv, Nn, 0.008, 0.016, 0.008)
    ellipsoid(mb, P(0.003, -0.012, 0.002), H, Vv, Nn, 0.004, 0.005, 0.004)  # tragus
    ob = mb.obj('Ear_' + ('L' if sgn > 0 else 'R'), MAT['skin'])
    add_mod(ob, 'REMESH', 'Remesh', mode='VOXEL', voxel_size=0.0011)
    add_mod(ob, 'SMOOTH', 'Smooth', factor=0.6, iterations=4)
    add_mod(ob, 'DECIMATE', 'Decimate', ratio=0.14)
    apply_mods(ob); smooth_all(ob)
    return ob
for s in (1, -1): build_ear(s)

# ------------------------------------------------------------------ HAIR
def head_dir(alpha, beta):
    return V(cos(beta), -sin(beta) * cos(alpha), sin(beta) * sin(alpha))

def scalp(alpha, beta):
    d = head_dir(alpha, beta)
    o = HC + V(0, 0, 0.005) + d * 0.4
    r = HBVH.ray_cast(o, -d)
    if r[0] is None: return HC + d * 0.1, d
    return r[0], r[1]

def bprime(beta): return min(1.0, abs(beta - pi / 2) / radians(62))
def alpha0(beta):
    bp = bprime(beta)
    return radians(42 + 34 * bp ** 1.5 - 3 * exp(-((beta - pi / 2) / radians(8)) ** 2))
def alpha1(beta): return radians(222 - 14 * bprime(beta) ** 2)
def hvol(alpha, beta):
    bp = bprime(beta)
    sa = max(0.0, sin(clamp(alpha, 0, pi)))
    v = 0.004 + 0.018 * sa ** 1.2 * (1 - 0.7 * bp ** 2)
    v += 0.009 * exp(-((alpha - radians(58)) / radians(16)) ** 2) * (1 - bp ** 2)
    v += 0.004 * ss(radians(200), radians(222), alpha)
    return v

def build_hair():
    # scalp cap
    mb = MB(); rows = []
    NB, NA = 34, 40
    for i in range(NB):
        beta = lerp(radians(26), radians(154), i / (NB - 1))
        a0, a1 = alpha0(beta) - radians(1.5), alpha1(beta)
        row = []
        for j in range(NA):
            u = j / (NA - 1); a = lerp(a0, a1, u)
            loc, nor = scalp(a, beta)
            edge = ss(0.0, 0.06, u) * ss(1.0, 0.94, u) * ss(0, 0.08, i / (NB - 1)) * ss(1, 0.92, i / (NB - 1))
            row.append(loc + nor * (0.0008 + hvol(a, beta) * 0.93 * edge))
        rows.append(row)
    mb.grid(rows)
    cap = mb.obj('Hair_Cap', MAT['hair'])
    # strands
    mb = MB()
    def strand(pts_fn, npts, width, thick, nsides=6):
        pts = []; nors = []
        for k in range(npts):
            p, nn = pts_fn(k / (npts - 1)); pts.append(p); nors.append(nn)
        rings = []
        for k in range(npts):
            u = k / (npts - 1)
            t = (pts[min(k + 1, npts - 1)] - pts[max(k - 1, 0)]).normalized()
            nn = (nors[k] - t * nors[k].dot(t)).normalized(); sd = t.cross(nn)
            tp = (0.12 + 0.88 * ss(0.0, 0.22, u)) * (1 - 0.8 * ss(0.75, 1.0, u))
            rings.append(ring(pts[k], sd, nn, width * 0.5 * tp + 0.0004, thick * 0.5 * tp + 0.0003, nsides))
        mb.add_rings(rings, cap0=True, cap1=True)
    NS = 64
    for i in range(NS):
        b0 = lerp(radians(28), radians(152), (i + random.uniform(-0.4, 0.4)) / (NS - 1))
        lay = random.uniform(0.95, 1.06)
        ph = random.uniform(0, 6.28); wob = radians(random.uniform(1.0, 3.0))
        da0 = radians(random.uniform(3, 9)); da1 = radians(random.uniform(-10, 4))
        w = random.uniform(0.020, 0.030) * (1 - 0.3 * bprime(b0)); th = random.uniform(0.0030, 0.0045)
        def fn(u, b0=b0, lay=lay, ph=ph, wob=wob, da0=da0, da1=da1):
            beta = b0 + wob * sin(u * 5 + ph) + radians(4) * (u - 0.5) * (1 if b0 > pi / 2 else -1) * 0.0
            a = lerp(alpha0(beta) + da0, alpha1(beta) + da1, u)
            loc, nor = scalp(a, beta)
            off = hvol(a, beta) * lay + 0.0015 + 0.006 * ss(0.85, 1.0, u)
            return loc + nor * off, nor
        strand(fn, 14, w, th, nsides=5)
    # loose forehead strands (character's right)
    for (b0, a_end, w, drift) in []:
        def fn(u, b0=b0, a_end=a_end, drift=drift):
            a = lerp(radians(80), a_end, u)
            beta = b0 + radians(drift) * u ** 1.5
            loc, nor = scalp(a, beta)
            off = lerp(hvol(a, beta) * 0.95, 0.004, ss(0.2, 0.9, u))
            return loc + nor * off, nor
        strand(fn, 14, w, 0.003)
    # sideburns
    for sgn in (1, -1):
        for k in range(3):
            b0 = radians(30 + k * 2.5) if sgn > 0 else radians(150 - k * 2.5)
            def fn(u, b0=b0, k=k):
                a = lerp(radians(78), radians(12 + k * 6), u)
                loc, nor = scalp(a, b0)
                return loc + nor * (0.0025 - 0.0015 * u), nor
            strand(fn, 10, 0.011, 0.0025)
    st = mb.obj('Hair_Strands', MAT['hair'])
    return cap, st
build_hair()

# ------------------------------------------------------------------ SUNGLASSES
def build_glasses():
    mb_l = MB(); mb_f = MB()
    W, H = 0.025, 0.0185
    def outline(k, N=36, sc=1.0):
        t = 2 * pi * k / N
        u = W * sgnpow(cos(t), 0.55); v = H * sgnpow(sin(t), 0.55)
        if v < 0: u *= 1 - 0.10 * (-v / H)
        if v > 0: u *= 1 + 0.03 * (v / H)
        return u * sc, v * sc
    zc = 0.011
    lens_info = {}
    for sgn in (1, -1):
        pin, _ = hit_front(sgn * 0.010, zc)
        pout, _ = hit_front(sgn * 0.057, zc)
        pcb, _ = hit_front(sgn * 0.050, -0.006)
        y_in = pin.y - 0.0045
        y_out = min(pout.y, pcb.y) - 0.0055
        xin, xout = sgn * 0.0095, sgn * (0.0095 + 2 * W * 0.97)
        cx = (xin + xout) / 2; cy = (y_in + y_out) / 2
        a = atan2(y_out - y_in, abs(xout - xin))
        Xd = V(sgn * cos(a), sin(a), 0); Nd = V(sgn * sin(a), -cos(a), 0)
        if Nd.y > 0: Nd = -Nd
        C = V(cx, cy, HC.z + zc)
        lens_info[sgn] = (C, Xd, Nd)
        def L3(u, v, bul=True):
            return C + Xd * u + ZA * v + Nd * ((0.0022 * (1 - (u / W) ** 2 - 0.5 * (v / H) ** 2)) if bul else 0)
        rings = []
        for s in (0.25, 0.5, 0.75, 1.0):
            rings.append([L3(*outline(k, sc=s)) for k in range(36)])
        mb_l.add_rings(rings, cap0=True, c0=L3(0, 0))
        # rim
        pts = [L3(*outline(k, sc=1.04), bul=False) + Nd * 0.0005 for k in range(36)]
        rads = [0.0021 + 0.0013 * ss(0.0, 0.8, outline(k)[1] / H) for k in range(36)]
        rads2 = [r * 1.25 for r in rads]
        tube_loop(mb_f, pts, rads, Nd, n=8, rads2=rads2)
        # temple arm
        hinge = L3(W * 1.05, H * 0.55, bul=False) - Nd * 0.002
        path = [hinge]
        for yy, zz in [(-0.045, 0.026), (-0.02, 0.028), (0.005, 0.030), (0.022, 0.027), (0.034, 0.012), (0.038, -0.002)]:
            loc, nor = hit_side(sgn, yy, zz)
            path.append(loc + V(sgn * 0.0035, 0, 0))
        path = resample(path, 18)
        tube(mb_f, path, [0.0016] * 18, n=6, rads2=[0.0028] * 18, up=V(sgn, 0, 0))
        box(mb_f, hinge, Xd, ZA, Nd, 0.0035, 0.004, 0.0028)
    # bridge
    pl = lens_info[1]; pr = lens_info[-1]
    b1 = pl[0] + pl[1] * (-W * 1.0) + ZA * (H * 0.45)
    b2 = pr[0] + pr[1] * (-W * 1.0) + ZA * (H * 0.45)
    bm_ = (b1 + b2) / 2 + V(0, -0.003, 0.003)
    tube(mb_f, resample([b1, bm_, b2], 10), [0.0024] * 10, n=8, rads2=[0.0032] * 10)
    # nose pads
    for sgn in (1, -1):
        loc, nor = hit_front(sgn * 0.0085, 0.0)
        ellipsoid(mb_f, loc + nor * 0.002, ZA, YA, XA, 0.004, 0.0025, 0.0015, nu=8, nv=5)
    lens = mb_l.obj('Sunglasses_Lenses', MAT['lens'])
    fr = mb_f.obj('Sunglasses_Frame', MAT['frame'])
build_glasses()

# ------------------------------------------------------------------ CIGARETTE
def build_cigarette():
    X = -0.021; Zm = mouth_z(X)
    M, nor = hit_front(X, Zm)
    d = V(-0.33, -1.0, -0.30).normalized()
    P0 = M + V(0, 0.005, 0)
    mb = MB(); cylinder(mb, P0, P0 + d * 0.024, 0.0043, n=14); f = mb.obj('Cig_Filter', MAT['filter'])
    mb = MB(); cylinder(mb, P0 + d * 0.024, P0 + d * 0.066, 0.0042, n=14); p = mb.obj('Cig_Paper', MAT['paper'])
    mb = MB(); cylinder(mb, P0 + d * 0.066, P0 + d * 0.071, 0.0041, n=14, r1=0.0038); a = mb.obj('Cig_Ash', MAT['ash'])
    mb = MB(); cylinder(mb, P0 + d * 0.0655, P0 + d * 0.0685, 0.00435, n=14); e = mb.obj('Cig_Ember', MAT['ember'])
    for o in (f, p, a, e): smooth_all(o)
build_cigarette()

def build_brows():
    mb = MB()
    for sgn in (1, -1):
        pts = []
        for k in range(8):
            t = k / 7
            x = sgn * lerp(0.011, 0.056, t); z = 0.031 + 0.006 * sin(t * pi) - 0.004 * t
            loc, nor = hit_front(x, z)
            pts.append(loc + nor * 0.0012)
        rr = [0.0032 * (1 - 0.55 * (k / 7) ** 2) for k in range(8)]
        tube(mb, pts, [r * 0.45 for r in rr], n=6, up=V(0, -1, 0.3), rads2=rr)
    mb.obj('Eyebrows', MAT['hair'])
build_brows()

HEAD_SCALE = 1.07
def scale_head_group():
    piv = V(0, 0.01, 1.60)
    M = Matrix.Translation(piv) @ Matrix.Scale(HEAD_SCALE, 4) @ Matrix.Translation(-piv)
    for o in list(CHAR.objects):
        if o.name.startswith(('Head', 'Ear_', 'Hair_', 'Sunglasses', 'Cig_', 'Eyebrows')):
            o.data.transform(M); o.data.update()
        if o.name == 'EmberGlow': o.location = M @ o.location
scale_head_group()

# ------------------------------------------------------------------ TORSO: SHIRT
SH = [(0.95, 0.148, 0.100, 0.0), (1.03, 0.156, 0.108, 0.0), (1.15, 0.163, 0.112, 0.0), (1.27, 0.180, 0.119, 0.003),
      (1.36, 0.188, 0.117, 0.006), (1.43, 0.188, 0.110, 0.010), (1.48, 0.172, 0.100, 0.012),
      (1.51, 0.118, 0.084, 0.013), (1.535, 0.068, 0.068, 0.013), (1.56, 0.060, 0.064, 0.013), (1.60, 0.058, 0.062, 0.012)]
def shirt_front(x, z, off=0.0):
    a, b, yo = interp(SH, z); e = 2.5
    xx = min(abs(x), 0.985 * a)
    y = yo - b * (1 - (xx / a) ** e) ** (1 / e)
    return V(x, y - off, z)

def build_shirt():
    mb = MB(); rings = []
    zs = [1.0 + i * 0.04 for i in range(13)] + [1.51, 1.535]
    for z in zs:
        a, b, yo = interp(SH, z)
        def mod(p, t, cx, cy, z=z):
            r = V(p.x, p.y - yo, 0).normalized()
            return p + r * (0.0025 * nz(V(p.x * 10, p.y * 10, p.z * 6)))
        rings.append(ring(V(0, yo, z), XA, YA, a, b, 32, e=2.5, mod=mod))
    mb.add_rings(rings, cap0=True)
    ob = mb.obj('Shirt', MAT['shirt']); subsurf(ob, 1)
    # collar band
    mb = MB(); rings = []
    for z, r1, r2, yc in [(1.515, 0.070, 0.074, 0.013), (1.555, 0.065, 0.069, 0.012), (1.595, 0.064, 0.068, 0.012)]:
        rings.append(ring(V(0, yc, z), XA, YA, r1, r2, 30, t0=radians(-72), t1=radians(252), closed=False))
    mb.add_rings(rings, closed=False)
    # collar points (flaps)
    for sgn in (1, -1):
        quad = [(0.018, 1.594), (0.050, 1.574), (0.062, 1.502), (0.026, 1.512)]
        rows = []
        for i in range(4):
            t = i / 3
            pa = V(lerp(quad[0][0], quad[3][0], t), 0, lerp(quad[0][1], quad[3][1], t))
            pb = V(lerp(quad[1][0], quad[2][0], t), 0, lerp(quad[1][1], quad[2][1], t))
            row = []
            for j in range(4):
                q = pa.lerp(pb, j / 3)
                row.append(shirt_front(sgn * q.x, q.z, 0.0065 + 0.003 * (1 - t)))
            rows.append(row)
        mb.grid(rows)
    col = mb.obj('Shirt_Collar', MAT['shirt'])
    add_mod(col, 'SOLIDIFY', 'Solidify', thickness=0.0025, offset=0.0); subsurf(col, 1)
build_shirt()

# ------------------------------------------------------------------ TIE
def build_tie():
    mb = MB(); rings = []
    for z, w, dpt in [(1.497, 0.011, 0.007), (1.505, 0.013, 0.010), (1.52, 0.016, 0.011), (1.535, 0.017, 0.010), (1.545, 0.015, 0.006)]:
        c = shirt_front(0.0, z, 0.0075 + dpt * 0.4)
        rings.append(ring(c, XA, YA, w, dpt * 0.6, 12, e=2.4))
    mb.add_rings(rings, cap0=True, cap1=True)
    knot = mb.obj('Tie_Knot', MAT['tie']); subsurf(knot, 1)
    mb = MB(); rows = []
    zs = [1.50 - i * 0.025 for i in range(19)]
    WK = [(1.03, 0.034), (1.08, 0.035), (1.12, 0.033), (1.30, 0.028), (1.45, 0.020), (1.50, 0.012)]
    for z in zs:
        w = interp(WK, z)[0] * 0.5 * 2
        sway = -0.004 * ss(1.45, 1.1, z) + 0.002 * sin(z * 9)
        row = []
        for j in range(5):
            u = -1 + 2 * j / 4
            p = shirt_front(sway + u * w * 0.88, z, 0.006 + 0.003 * (1 - u * u))
            row.append(p)
        rows.append(row)
    tip = shirt_front(0.0 - 0.004, 1.012, 0.0065)
    last = rows[-1]
    rows.append([last[0].lerp(tip, 0.6), last[1].lerp(tip, 0.8), tip + V(0, -0.0005, 0), last[3].lerp(tip, 0.8), last[4].lerp(tip, 0.6)])
    mb.grid(rows)
    blade = mb.obj('Tie_Blade', MAT['tie'])
    add_mod(blade, 'SOLIDIFY', 'Solidify', thickness=0.003, offset=0.0); subsurf(blade, 1)
build_tie()

# ------------------------------------------------------------------ PANTS + BELT
PV = [(0.92, 0.150, 0.094), (0.96, 0.158, 0.104), (1.00, 0.162, 0.111), (1.048, 0.164, 0.114)]
def build_pants():
    mb = MB(); rings = []
    for z in [0.92, 0.95, 0.98, 1.01, 1.048]:
        a, b = interp(PV, z)
        rings.append(ring(V(0, 0, z), XA, YA, a, b, 32, e=2.4))
    mb.add_rings(rings, cap0=True, c0=V(0, 0.01, 0.90))
    pel = mb.obj('Pants_Pelvis', MAT['suit']); subsurf(pel, 1)
    LK = [(0.0, 0.094, 0.104), (0.10, 0.089, 0.096), (0.25, 0.078, 0.082), (0.42, 0.066, 0.069), (0.52, 0.060, 0.063),
          (0.62, 0.062, 0.065), (0.78, 0.057, 0.059), (0.92, 0.054, 0.057), (1.0, 0.057, 0.060)]
    for sgn in (1, -1):
        mb = MB(); rings = []
        C0 = V(sgn * 0.083, 0.0, 1.0); C1 = V(sgn * 0.150, 0.004, 0.062)
        NR = 26
        for i in range(NR):
            t = i / (NR - 1); c = C0.lerp(C1, t)
            rx, ry = interp(LK, t)
            def mod(p, ang, cx, cy, t=t, c=c):
                r = V(p.x - c.x, p.y - c.y, 0).normalized()
                off = 0.0028 * nz(V(p.x * 14, p.y * 14, p.z * 5) + V(sgn * 3, 0, 0))
                off += 0.0035 * sin(p.z * 190 + 3 * nz(V(p.x * 20, p.y * 20, p.z * 3))) * ss(0.80, 0.92, t)
                off += 0.0028 * nz(V(p.x * 6, p.y * 6, p.z * 55)) * exp(-((t - 0.52) / 0.07) ** 2)
                dz = ((-cy) * 0.0125 + 0.0025) * ss(0.9, 1.0, t)
                return p + r * off + V(0, 0, dz)
            rings.append(ring(c, XA, YA, rx, ry, 22, mod=mod))
        mb.add_rings(rings)
        leg = mb.obj('Pants_Leg_' + ('L' if sgn > 0 else 'R'), MAT['suit'])
        subsurf(leg, 1)
build_pants()

def build_belt():
    mb = MB(); rings = []
    for z in (1.004, 1.025, 1.046):
        rings.append(ring(V(0, 0, z), XA, YA, 0.168, 0.117, 48, e=2.4))
    mb.add_rings(rings)
    belt = mb.obj('Belt', MAT['leather'])
    add_mod(belt, 'SOLIDIFY', 'Solidify', thickness=0.004, offset=1.0); subsurf(belt, 1)
    # buckle
    mb = MB()
    fy = -0.120 - 0.006
    pts = []
    for k in range(28):
        t = 2 * pi * k / 28
        pts.append(V(0.0 + 0.026 * sgnpow(cos(t), 0.35), fy, 1.025 + 0.021 * sgnpow(sin(t), 0.35)))
    tube_loop(mb, pts, [0.0035] * 28, V(0, -1, 0), n=8, rads2=[0.0022] * 28)
    tube(mb, [V(-0.024, fy, 1.025), V(0.0, fy - 0.004, 1.025)], [0.0018, 0.0018], n=6)
    bk = mb.obj('Belt_Buckle', MAT['metal'])
    # tail end hanging
    mb = MB(); rows = []
    def bfront(x, z, off):
        yy = 0.120 * (1 - (abs(x) / 0.170) ** 2.4) ** (1 / 2.4)
        return V(x, -yy - off, z)
    path = [bfront(0.030, 1.025, 0.0075), bfront(0.055, 1.025, 0.0075), bfront(0.078, 1.024, 0.0075), bfront(0.094, 1.016, 0.0072), bfront(0.101, 1.003, 0.007)]
    path = resample(path, 9)
    for i, p in enumerate(path):
        tcur = (path[min(i + 1, 8)] - path[max(i - 1, 0)]).normalized()
        side = tcur.cross(V(0, -1, 0)).normalized()
        w = 0.0185 if i < 8 else 0.008
        rows.append([p - side * w, p, p + side * w])
    mb.grid(rows)
    tail = mb.obj('Belt_Tail', MAT['leather'])
    add_mod(tail, 'SOLIDIFY', 'Solidify', thickness=0.004, offset=0.0); subsurf(tail, 1)
build_belt()

# ------------------------------------------------------------------ JACKET
JK = [(0.855, 0.192, 0.137, 0.000, 2.7), (0.95, 0.182, 0.129, 0.0, 2.7), (1.03, 0.174, 0.123, 0.0, 2.6),
      (1.15, 0.180, 0.126, 0.0, 2.6), (1.27, 0.197, 0.133, 0.003, 2.6), (1.36, 0.207, 0.131, 0.006, 2.5),
      (1.43, 0.210, 0.123, 0.010, 2.4), (1.48, 0.202, 0.113, 0.012, 2.3), (1.505, 0.166, 0.101, 0.014, 2.2),
      (1.525, 0.116, 0.086, 0.015, 2.1), (1.538, 0.079, 0.073, 0.015, 2.0)]
XIN = [(0.855, 0.125), (0.95, 0.098), (1.03, 0.082), (1.12, 0.073), (1.25, 0.063), (1.40, 0.054), (1.48, 0.051), (1.538, 0.050)]

def jacket_front(x, z, off=0.0):
    a, b, yo, e = interp(JK, z)
    xx = min(abs(x), 0.985 * a)
    yy = b * (1 - (xx / a) ** e) ** (1 / e)
    y = yo - yy
    nx = (xx / a) ** (e - 1) / a * (1 if x >= 0 else -1); ny = -((yy / b) ** (e - 1)) / b
    n = V(nx, ny, 0).normalized()
    return V(x, y, z) + n * off

def build_jacket():
    zs = []
    keys = [k[0] for k in JK]
    for i in range(len(keys) - 1):
        z0, z1 = keys[i], keys[i + 1]
        m = max(1, int(math.ceil((z1 - z0) / 0.042)))
        for k in range(m): zs.append(lerp(z0, z1, k / m))
    zs.append(keys[-1])
    mb = MB(); rings = []
    NJ = 42
    for z in zs:
        a, b, yo, e = interp(JK, z); xin = interp(XIN, z)[0]
        c = (min(xin / a, 0.97)) ** (e / 2)
        ts, te = -acos(c), pi + acos(c)
        def mod(p, t, cx, cy, z=z, yo=yo):
            r = V(p.x, p.y - yo, 0).normalized()
            front_quiet = 1 - ss(1.08, 1.16, z) * ss(-0.1, -0.5, cy)
            hem = ss(0.93, 0.86, z)
            off = 0.0035 * nz(V(p.x * 9, p.y * 9, p.z * 4))
            off += 0.0030 * nz(V(p.x * 3, p.y * 3, p.z * 26)) * (ss(1.18, 1.0, z) + 0.5 * ss(0.3, 0.9, cy))
            off += 0.004 * hem * nz(V(p.x * 18, p.y * 18, 1.0))
            off *= front_quiet
            off += 0.006 * ss(0.92, 0.855, z)  # hem flare
            return p + r * off
        rings.append(ring(V(0, yo, z), XA, YA, a, b, NJ, e=e, t0=ts, t1=te, closed=False, mod=mod))
    mb.add_rings(rings, closed=False)
    body = mb.obj('Jacket_Body', MAT['suit'])
    add_mod(body, 'SOLIDIFY', 'Solidify', thickness=0.006, offset=-1.0, use_even_offset=True)
    subsurf(body, 1)

    # lapels
    inner = [(0.073, 1.12), (0.066, 1.22), (0.058, 1.33), (0.053, 1.42), (0.051, 1.48), (0.055, 1.536)]
    outer = [(0.075, 1.12), (0.100, 1.22), (0.127, 1.32), (0.141, 1.383), (0.118, 1.394), (0.123, 1.422),
             (0.110, 1.48), (0.086, 1.537)]
    K = 14
    ri = resample([V(x, 0, z) for x, z in inner], K); ro = resample([V(x, 0, z) for x, z in outer], K)
    mb = MB()
    for sgn in (1, -1):
        rows = []
        for k in range(K):
            row = []
            for j in range(4):
                t = j / 3
                q = ri[k].lerp(ro[k], t)
                row.append(jacket_front(sgn * q.x, q.z, lerp(0.014, 0.0065, t) * (1 - 0.5 * ss(1.45, 1.53, q.z)) + 0.003))
            rows.append(row)
        mb.grid(rows)
    lap = mb.obj('Jacket_Lapels', MAT['suit'])
    add_mod(lap, 'SOLIDIFY', 'Solidify', thickness=0.005, offset=0.0); subsurf(lap, 1)

    # back collar
    mb = MB(); rings = []
    for z, r1, r2, yc in [(1.528, 0.082, 0.078, 0.015), (1.555, 0.077, 0.073, 0.017), (1.578, 0.073, 0.069, 0.018)]:
        rings.append(ring(V(0, yc, z), XA, YA, r1, r2, 26, t0=radians(-40), t1=radians(220), closed=False))
    mb.add_rings(rings, closed=False)
    colr = mb.obj('Jacket_Collar', MAT['suit'])
    add_mod(colr, 'SOLIDIFY', 'Solidify', thickness=0.004, offset=0.0); subsurf(colr, 1)

    # pocket flaps + buttons
    mb = MB()
    for sgn in (1, -1):
        rows = []
        for zz in (0.988, 0.968, 0.952):
            rows.append([jacket_front(sgn * lerp(0.104, 0.172, j / 5), zz, 0.006 + 0.003 * nz(V(j, zz * 50, sgn))) for j in range(6)])
        mb.grid(rows)
    fl = mb.obj('Jacket_PocketFlaps', MAT['suit'])
    add_mod(fl, 'SOLIDIFY', 'Solidify', thickness=0.003, offset=0.0); subsurf(fl, 1)
    mb = MB()
    for z in (1.06, 0.965):
        xin = interp(XIN, z)[0]
        p = jacket_front(xin + 0.016, z, 0.004)
        n = (jacket_front(xin + 0.016, z, 0.01) - p).normalized()
        cylinder(mb, p, p + n * 0.0035, 0.0095, n=16, r1=0.0085)
    # sleeve buttons
    for sgn in (1, -1):
        S, D, U = arm_frame(sgn)
        for k in range(3):
            c = S + D * (0.49 - k * 0.022) + V(0, 0.032, 0) - U * 0.026
            n = (V(0, 0.6, -0.8) if True else V(0, 1, 0)).normalized()
            cylinder(mb, c, c + n * 0.003, 0.0055, n=12)
    btn = mb.obj('Jacket_Buttons', MAT['button'])

    # sleeves
    SK = [(-0.05, 0.068, 0.058), (0.0, 0.071, 0.064), (0.05, 0.067, 0.063), (0.15, 0.062, 0.058), (0.27, 0.056, 0.053),
          (0.35, 0.054, 0.051), (0.45, 0.050, 0.047), (0.53, 0.047, 0.045), (0.545, 0.047, 0.045)]
    for sgn in (1, -1):
        S, D, U = arm_frame(sgn)
        F = V(0, -1, 0)
        mb = MB(); rings = []
        NSR = 24
        for i in range(NSR):
            s = lerp(-0.05, 0.545, i / (NSR - 1))
            ry, rz = interp(SK, s)
            c = S + D * s
            def mod(p, t, cx, cy, s=s, c=c):
                r = (p - c); r = (r - D * r.dot(D)).normalized()
                off = 0.003 * nz(V(p.x * 4, p.y * 12, p.z * 12) + V(sgn * 5, 0, 0))
                off += 0.0035 * nz(V(p.x * 40, p.y * 3, p.z * 3)) * exp(-((s - 0.28) / 0.06) ** 2)
                off += 0.003 * sin(s * 170 + 2 * nz(V(p.x * 10, p.y * 10, p.z * 10))) * ss(0.43, 0.5, s)
                return p + r * off
            rings.append(ring(c, F, U, ry, rz, 22, mod=mod))
        mb.add_rings(rings)
        sl = mb.obj('Jacket_Sleeve_' + ('L' if sgn > 0 else 'R'), MAT['suit'])
        subsurf(sl, 1)
        # shirt cuff
        mb = MB(); rings = []
        for s, r in [(0.50, 0.040), (0.535, 0.040), (0.562, 0.0385)]:
            rings.append(ring(S + D * s, F, U, r, r * 0.94, 20))
        mb.add_rings(rings)
        cf = mb.obj('Shirt_Cuff_' + ('L' if sgn > 0 else 'R'), MAT['shirt'])
        add_mod(cf, 'SOLIDIFY', 'Solidify', thickness=0.003, offset=-1.0); subsurf(cf, 1)
build_jacket()

# ------------------------------------------------------------------ HANDS
HAND_JOINTS = {}
def build_hand(sgn):
    S, D, U = arm_frame(sgn)
    L = V(0, -1, 0)
    W = S + D * 0.552
    def P(f, l, u): return W + D * f + L * l + U * u
    def Pv(v): return P(v.x, v.y, v.z)
    mb = MB()
    rings = []
    for f, hw, ht in [(-0.02, 0.025, 0.018), (0.0, 0.027, 0.0175), (0.02, 0.031, 0.0168), (0.06, 0.039, 0.0158),
                      (0.088, 0.042, 0.0142), (0.103, 0.039, 0.0115)]:
        rings.append(ring(P(f, 0.002, 0.0), L, U, hw, ht, 16, e=2.6))
    mb.add_rings(rings, cap0=True, cap1=True)
    def fdir(sp, b):
        sp, b = radians(sp), radians(b)
        return V(cos(sp) * cos(b), sin(sp) * cos(b), -sin(b))
    fingers = [(0.027, 0.093, 6, (0.042, 0.026, 0.021), 0.0088),
               (0.009, 0.098, 1.5, (0.046, 0.029, 0.023), 0.0090),
               (-0.010, 0.095, -3.5, (0.043, 0.027, 0.022), 0.0085),
               (-0.027, 0.086, -10, (0.034, 0.021, 0.019), 0.0074)]
    bends = (9, 15, 11)
    HAND_JOINTS[sgn] = {}
    for fname, (l0, f0, sp, lens, r0) in zip(('Index', 'Middle', 'Ring', 'Pinky'), fingers):
        J = [V(f0 - 0.016, l0 * 0.9, 0.001), V(f0, l0, 0.002)]
        b = 0
        for k in range(3):
            b += bends[k] + (4 if l0 < -0.02 else 0)
            J.append(J[-1] + fdir(sp, b) * lens[k])
        R = [r0 * 1.05, r0, r0 * 0.9, r0 * 0.8, r0 * 0.68]
        HAND_JOINTS[sgn][fname] = [Pv(j) for j in J[1:]]
        pts = []; rr = []
        for k in range(len(J) - 1):
            for m in range(3 if k > 0 else 1):
                t = m / (3 if k > 0 else 1)
                pts.append(J[k].lerp(J[k + 1], t)); rr.append(lerp(R[k], R[k + 1], t))
        pts.append(J[-1]); rr.append(R[-1])
        tube(mb, [Pv(p) for p in pts], [r * 0.86 for r in rr], n=10, up=U, rads2=rr)
        ellipsoid(mb, P(f0, l0, 0.0075), D, L, U, 0.008, 0.0075, 0.0045, nu=10, nv=6)
        # fingernail
        tipd = fdir(sp, b)
        nc = J[-1] - tipd * 0.006
        ellipsoid(mb, Pv(nc + V(0, 0, R[-1] * 0.72)), D, L, U, 0.0065, R[-1] * 0.72, 0.0012, nu=10, nv=5)
    J0 = V(0.022, 0.026, -0.006)
    d1 = V(0.55, 0.76, -0.20).normalized(); d2 = V(0.80, 0.52, -0.22).normalized(); d3 = V(0.88, 0.36, -0.28).normalized()
    J1 = J0 + d1 * 0.042; J2 = J1 + d2 * 0.031; J3 = J2 + d3 * 0.026
    pts = [J0 - d1 * 0.01] + [J0.lerp(J1, t) for t in (0, 0.5)] + [J1.lerp(J2, t) for t in (0, 0.5)] + [J2.lerp(J3, t) for t in (0, 0.5, 1.0)]
    rr = [0.013, 0.0135, 0.0122, 0.0108, 0.0101, 0.0095, 0.0087, 0.0078]
    tube(mb, [Pv(p) for p in pts], [r * 0.85 for r in rr], n=10, up=U, rads2=rr)
    ellipsoid(mb, P(0.035, 0.02, -0.008), D, L, U, 0.030, 0.016, 0.011)
    ellipsoid(mb, P(0.05, -0.028, -0.006), D, L, U, 0.035, 0.012, 0.009)
    HAND_JOINTS[sgn]['Thumb'] = [Pv(J0), Pv(J1), Pv(J2), Pv(J3)]
    ob = mb.obj('Hand_' + ('L' if sgn > 0 else 'R'), MAT['skin'])
    add_mod(ob, 'REMESH', 'Remesh', mode='VOXEL', voxel_size=0.0017)
    add_mod(ob, 'SMOOTH', 'Smooth', factor=0.5, iterations=3)
    add_mod(ob, 'DECIMATE', 'Decimate', ratio=0.11)
    apply_mods(ob); smooth_all(ob)
    return ob
for s in (1, -1): build_hand(s)

# ------------------------------------------------------------------ SHOES
def build_shoe(sgn):
    O = V(sgn * 0.150, 0.0, 0.0)
    Fd = V(sgn * sin(radians(7)), -cos(radians(7)), 0)
    Ld = V(Fd.y, -Fd.x, 0)
    keys = [(-0.060, 0.031, 0.078), (-0.052, 0.039, 0.098), (-0.032, 0.044, 0.114), (0.0, 0.045, 0.124),
            (0.03, 0.046, 0.113), (0.06, 0.046, 0.094), (0.10, 0.047, 0.073), (0.14, 0.045, 0.057),
            (0.175, 0.040, 0.047), (0.205, 0.031, 0.040), (0.228, 0.018, 0.034)]
    def zb(f): return 0.027 - 0.016 * ss(0.0, 0.06, f) + 0.008 * ss(0.17, 0.235, f)
    mb = MB(); rings = []
    for f, w, h in keys:
        b0 = zb(f); c = O + Fd * f + V(0, 0, (b0 + h) / 2)
        def mod(p, t, cx, cy, f=f):
            return p + V(0, 0, 0.0018 * nz(V(p.x * 60, p.y * 60, p.z * 60))) * ss(0.02, 0.06, f) * ss(0.16, 0.10, f) * (cy > 0)
        rings.append(ring(c, Ld, ZA, w, (h - b0) / 2, 24, e=2.5, mod=mod))
    mb.add_rings(rings, cap0=True, cap1=True, c0=O + Fd * (-0.066) + V(0, 0, 0.052), c1=O + Fd * 0.234 + V(0, 0, 0.032))
    up = mb.obj('Shoe_Upper_' + ('L' if sgn > 0 else 'R'), MAT['shoe']); subsurf(up, 1)
    # sole
    mb = MB(); rings = []
    for f, w, h in keys:
        top = zb(f) + 0.004; bot = 0.008 * ss(0.17, 0.235, f) + 0.011 * ss(0.012, 0.03, f) * ss(0.10, 0.075, f)
        c = O + Fd * f + V(0, 0, (top + bot) / 2)
        rings.append(ring(c, Ld, ZA, w + 0.0035, (top - bot) / 2, 24, e=8))
    mb.add_rings(rings, cap0=True, cap1=True, c0=O + Fd * (-0.066) + V(0, 0, 0.017), c1=O + Fd * 0.234 + V(0, 0, 0.021))
    so = mb.obj('Shoe_Sole_' + ('L' if sgn > 0 else 'R'), MAT['sole']); subsurf(so, 1)
    # laces
    mb = MB()
    def top_at(f, l):
        w, h = interp([(k[0], k[1], k[2]) for k in keys], f)
        return O + Fd * f + Ld * l + V(0, 0, h - 0.0012 * (l / 0.012) ** 2 + 0.0018)
    for f in (0.030, 0.048, 0.066, 0.084, 0.102):
        tube(mb, resample([top_at(f - 0.006, -0.013), top_at(f, 0.0) + V(0, 0, 0.001), top_at(f + 0.006, 0.013)], 6), [0.0017] * 6, n=6)
        tube(mb, resample([top_at(f + 0.006, -0.013), top_at(f, 0.0) + V(0, 0, 0.0025), top_at(f - 0.006, 0.013)], 6), [0.0017] * 6, n=6)
    for l in (-0.0135, 0.0135):  # eyelet rows
        for f in (0.024, 0.042, 0.060, 0.078, 0.096, 0.108):
            p = top_at(f, l); cylinder(mb, p - V(0, 0, 0.0008), p + V(0, 0, 0.0012), 0.0024, n=8)
    # toe-cap seam + welt
    for fseam in (0.150,):
        w, h = interp([(k[0], k[1], k[2]) for k in keys], fseam); b0 = zb(fseam)
        cc = O + Fd * fseam + V(0, 0, (b0 + h) / 2)
        seam = ring(cc, Ld, ZA, w + 0.0012, (h - b0) / 2 + 0.0012, 16, e=2.5, t0=radians(15), t1=radians(165), closed=False)
        seam = [p + Fd * (0.012 * sin(pi * k / 15)) for k, p in enumerate(seam)]
        tube(mb, seam, [0.0011] * 16, n=5)
    la = mb.obj('Shoe_Laces_' + ('L' if sgn > 0 else 'R'), MAT['sole'])
for s in (1, -1): build_shoe(s)

# ------------------------------------------------------------------ GUN + HOLSTER
def build_gun():
    Fw = V(0, 0.17, -0.985).normalized()
    Uw = V(0, 0.985, 0.17).normalized()
    Sw = Fw.cross(Uw)
    yaw = Matrix.Rotation(radians(-12), 3, 'Z')
    Fw, Uw, Sw = yaw @ Fw, yaw @ Uw, yaw @ Sw
    G0 = V(-0.214, 0.010, 1.085)
    def P(f, u, s=0.0): return G0 + Fw * f + Uw * u + Sw * s
    mb = MB()
    box(mb, P(0.075, 0.0165), Fw, Uw, Sw, 0.095, 0.0145, 0.0115)    # slide
    box(mb, P(0.060, -0.006), Fw, Uw, Sw, 0.075, 0.008, 0.011)      # frame
    g = (Fw * -sin(radians(18)) + Uw * -cos(radians(18))).normalized()
    gp = g.cross(Sw).normalized()
    if gp.dot(Fw) < 0: gp = -gp
    box(mb, P(0.004, -0.012) + g * 0.048, gp, g, Sw, 0.017, 0.050, 0.0135)  # grip
    box(mb, P(-0.024, 0.022), Fw, Uw, Sw, 0.006, 0.008, 0.0045)          # hammer
    box(mb, P(0.155, 0.033), Fw, Uw, Sw, 0.004, 0.003, 0.002)            # front sight
    box(mb, P(-0.008, 0.033), Fw, Uw, Sw, 0.004, 0.003, 0.006)           # rear sight
    cylinder(mb, P(0.168, 0.017), P(0.176, 0.017), 0.0085, n=14)
    tg = [P(0.058, -0.013), P(0.062, -0.028), P(0.045, -0.037), P(0.022, -0.033), P(0.013, -0.016)]
    tube(mb, resample(tg, 10), [0.0024] * 10, n=6, up=Sw, rads2=[0.0035] * 10)
    box(mb, P(0.034, -0.022), Fw, Uw, Sw, 0.0025, 0.008, 0.0028)
    gun = mb.obj('Pistol', MAT['gunmetal'])
    add_mod(gun, 'BEVEL', 'Bevel', width=0.0012, segments=2, limit_method='ANGLE')
    mb = MB()
    for s in (1, -1):
        box(mb, P(0.004, -0.012) + g * 0.050 + Sw * (s * 0.0138), gp, g, Sw, 0.014, 0.042, 0.0012)
    grip = mb.obj('Pistol_Grips', MAT['wood'])
    add_mod(grip, 'BEVEL', 'Bevel', width=0.0008, segments=2)
    # holster
    mb = MB(); rings = []
    for f, hu, hs, uc in [(0.050, 0.027, 0.019, 0.012), (0.07, 0.028, 0.0195, 0.010), (0.10, 0.028, 0.0185, 0.010),
                          (0.16, 0.025, 0.017, 0.010), (0.19, 0.020, 0.015, 0.012), (0.20, 0.012, 0.010, 0.012)]:
        def mod(p, t, cx, cy, f=f):
            return p + (p - P(f, uc)).normalized() * 0.0015 * nz(p * 80)
        rings.append(ring(P(f, uc), Uw, Sw, hu, hs, 18, e=2.6, mod=mod))
    mb.add_rings(rings, cap1=True)
    # belt loop strap
    box(mb, P(0.03, 0.035, 0.0), Fw, Uw, Sw, 0.06, 0.004, 0.012)
    hol = mb.obj('Holster', MAT['holster'])
    add_mod(hol, 'SOLIDIFY', 'Solidify', thickness=0.0035, offset=1.0); subsurf(hol, 1)
    return gun
build_gun()

# ------------------------------------------------------------------ RIG + SKIN WEIGHTS
def build_rig():
    arm = bpy.data.armatures.new('VSW_Rig')
    rig = bpy.data.objects.new('VSW_Rig', arm); CHAR.objects.link(rig)
    rig.show_in_front = True
    bpy.context.view_layer.objects.active = rig; rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    E = {}
    def bone(name, h, t, parent=None, connect=False):
        b = arm.edit_bones.new(name); b.head = h; b.tail = t
        if parent: b.parent = E[parent]; b.use_connect = connect
        E[name] = b; return b
    bone('Root', V(0, 0, 0), V(0, 0.25, 0))
    bone('Hips', V(0, 0.0, 0.98), V(0, 0.0, 1.08), 'Root')
    bone('Spine', V(0, 0.0, 1.08), V(0, 0.004, 1.25), 'Hips', True)
    bone('Chest', V(0, 0.004, 1.25), V(0, 0.010, 1.45), 'Spine', True)
    bone('Neck', V(0, 0.012, 1.47), V(0, 0.008, 1.62), 'Chest')
    bone('Head', V(0, 0.008, 1.62), V(0, 0.008, 1.86), 'Neck', True)
    for sgn, sfx in ((1, '.L'), (-1, '.R')):
        S, D, U = arm_frame(sgn)
        bone('Shoulder' + sfx, V(sgn * 0.03, 0.004, 1.45), S, 'Chest')
        bone('UpperArm' + sfx, S, S + D * 0.28, 'Shoulder' + sfx, True)
        bone('ForeArm' + sfx, S + D * 0.28, S + D * 0.552, 'UpperArm' + sfx, True)
        Wr = S + D * 0.552
        bone('Hand' + sfx, Wr, Wr + D * 0.09, 'ForeArm' + sfx, True)
        for fname, js in HAND_JOINTS[sgn].items():
            prev = 'Hand' + sfx
            for k in range(len(js) - 1):
                nm = '%s%d%s' % (fname, k + 1, sfx)
                bone(nm, js[k], js[k + 1], prev, k > 0); prev = nm
        C0 = V(sgn * 0.088, 0.0, 0.95); K = V(sgn * 0.119, -0.006, 0.50); A = V(sgn * 0.148, 0.002, 0.088)
        bone('Thigh' + sfx, C0, K, 'Hips')
        bone('Shin' + sfx, K, A, 'Thigh' + sfx, True)
        Fd = V(sgn * sin(radians(7)), -cos(radians(7)), 0)
        ball = V(sgn * 0.15, 0, 0) + Fd * 0.15 + V(0, 0, 0.025)
        bone('Foot' + sfx, A, ball, 'Shin' + sfx, True)
        bone('Toe' + sfx, ball, V(sgn * 0.15, 0, 0) + Fd * 0.228 + V(0, 0, 0.025), 'Foot' + sfx, True)
    bpy.ops.object.mode_set(mode='OBJECT')
    return rig

def arm_w(p, sgn):
    S, D, U = arm_frame(sgn); s = (p - S).dot(D); sf = '.L' if sgn > 0 else '.R'
    return {'Shoulder' + sf: 1 - ss(-0.08, 0.10, s),
            'UpperArm' + sf: ss(-0.08, 0.10, s) * (1 - ss(0.24, 0.32, s)),
            'ForeArm' + sf: ss(0.24, 0.32, s) * (1 - ss(0.535, 0.575, s)),
            'Hand' + sf: ss(0.535, 0.575, s)}

def skin_all(rig):
    B = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in rig.data.bones}
    def segd(p, a, b):
        ab = b - a; t = clamp((p - a).dot(ab) / ab.length_squared, 0, 1); return (p - (a + ab * t)).length
    def fingers(sfx): return [n for n in B if n.endswith(sfx) and n[:-2].rstrip('0123') in ('Thumb', 'Index', 'Middle', 'Ring', 'Pinky')]
    torso = ['Hips', 'Spine', 'Chest', 'Neck']
    arms_bias = {'UpperArm.L': 1.9, 'UpperArm.R': 1.9, 'Shoulder.L': 1.3, 'Shoulder.R': 1.3}
    def rule(name):
        if name.startswith(('Head', 'Ear_', 'Hair_', 'Eyebrows', 'Sunglasses', 'Cig_')): return ('rigid', 'Head')
        if name in ('Belt', 'Belt_Buckle', 'Belt_Tail', 'Holster', 'Pistol', 'Pistol_Grips'): return ('rigid', 'Hips')
        if name == 'Neck': return (['Neck', 'Head', 'Chest'], 4, 2, {})
        if name in ('Jacket_Body', 'Jacket_Lapels', 'Jacket_Collar', 'Jacket_PocketFlaps'): return (torso, 4, 2, {}, 'shoulder')
        if name in ('Shirt', 'Shirt_Collar', 'Tie_Knot', 'Tie_Blade'): return (torso, 4, 2, {}, 'shoulder')
        if name == 'Jacket_Buttons': return (['Hips', 'Spine', 'ForeArm.L', 'ForeArm.R'], 8, 1, {})
        for sfx, side in (('_L', '.L'), ('_R', '.R')):
            if name.endswith(sfx):
                if name.startswith(('Jacket_Sleeve', 'Shirt_Cuff')): return ('arm', 1 if side == '.L' else -1)
                if name.startswith('Hand'): return (['Hand' + side] + fingers(side), 8, 2, {'Hand' + side: 0.8}, 'hand')
                if name.startswith('Pants_Leg'): return (['Hips', 'Thigh' + side, 'Shin' + side, 'Foot' + side], 4, 2, {'Hips': 1.4})
                if name.startswith('Shoe'): return (['Shin' + side, 'Foot' + side, 'Toe' + side], 6, 2, {'Shin' + side: 2.0})
        if name == 'Pants_Pelvis': return (['Hips', 'Spine', 'Thigh.L', 'Thigh.R'], 4, 2, {})
        return ('rigid', 'Hips')
    for ob in list(CHAR.objects):
        if ob.type != 'MESH': continue
        r = rule(ob.name)
        G = {}
        def put(vi, wd):
            for n, w in wd.items():
                if w > 0.01:
                    if n not in G: G[n] = ob.vertex_groups.new(name=n)
                    G[n].add([vi], w, 'REPLACE')
        if r[0] == 'rigid':
            vg = ob.vertex_groups.new(name=r[1]); vg.add(list(range(len(ob.data.vertices))), 1.0, 'REPLACE')
        elif r[0] == 'arm':
            for v in ob.data.vertices: put(v.index, arm_w(v.co, r[1]))
        else:
            names, pw, top, bias = r[:4]; post = r[4] if len(r) > 4 else None
            for v in ob.data.vertices:
                p = v.co
                ds = sorted((segd(p, *B[n]) * bias.get(n, 1.0), n) for n in names)[:top]
                ws = [(1.0 / max(d, 1e-4)) ** pw for d, n in ds]; s = sum(ws)
                wd = {n: w / s for (d, n), w in zip(ds, ws)}
                if post == 'shoulder':
                    sg = 1 if p.x > 0 else -1
                    k = ss(0.08, 0.17, abs(p.x)) * ss(1.30, 1.43, p.z)
                    aw = arm_w(p, sg)
                    wd = {n: w * (1 - k) for n, w in wd.items()}
                    for n, w in aw.items(): wd[n] = wd.get(n, 0) + w * k
                elif post == 'hand':
                    sg = 1 if p.x > 0 else -1
                    aw = arm_w(p, sg); h = aw.get('Hand' + ('.L' if sg > 0 else '.R'), 0)
                    wd = {n: w * h for n, w in wd.items()}
                    for n, w in aw.items():
                        if not n.startswith('Hand'): wd[n] = wd.get(n, 0) + w
                put(v.index, wd)
        m = ob.modifiers.new('Armature', 'ARMATURE'); m.object = rig; m.use_deform_preserve_volume = True
        try: ob.modifiers.move(len(ob.modifiers) - 1, 0)
        except Exception:
            with bpy.context.temp_override(object=ob):
                bpy.ops.object.modifier_move_to_index(modifier='Armature', index=0)
        ob.parent = rig

RIG = build_rig()
skin_all(RIG)

# ------------------------------------------------------------------ ENVIRONMENT
def build_env():
    # ground
    me = bpy.data.meshes.new('Ground')
    me.from_pydata([(-12, -12, 0), (12, -12, 0), (12, 12, 0), (-12, 12, 0)], [], [(0, 1, 2, 3)])
    g = bpy.data.objects.new('Ground', me); ENV.objects.link(g)
    m = pbr('Wet_Asphalt', (0.022, 0.022, 0.025), rough=0.45,
            var=[((0.008, 0.008, 0.01), 0.35, 0.45, 0.6, None), ((0.05, 0.048, 0.05), 6, 0.6, 0.9, None)],
            bumps=[('noise', 60, 0.35, 0.01), ('voronoi', 9, 0.15, 0.01)], rough_var=(0.03, 0.7, 0.45))
    me.materials.append(m)
    # alley walls
    wall = pbr('Alley_Brick', (0.05, 0.035, 0.03), rough=0.85, var=[((0.015, 0.012, 0.012), 3, 0.4, 0.8, None)],
               bumps=[('noise', 25, 0.4, 0.02)])
    nt = wall.node_tree; br = nt.nodes.new('ShaderNodeTexBrick'); br.inputs['Scale'].default_value = 6
    set_in(br, 'Color1', (0.06, 0.035, 0.03, 1)); set_in(br, 'Color2', (0.035, 0.025, 0.022, 1)); set_in(br, 'Mortar', (0.015, 0.015, 0.015, 1))
    tc = nt.nodes.new('ShaderNodeTexCoord'); nt.links.new(tc.outputs['UV'], br.inputs['Vector']); br.inputs['Scale'].default_value = 1.6; set_in(br, 'Brick Width', 0.25); set_in(br, 'Row Height', 0.075); set_in(br, 'Mortar Size', 0.008)
    bsdf = nt.nodes.get('Principled BSDF'); nt.links.new(br.outputs['Color'], bsdf.inputs['Base Color'])
    for sgn in (1, -1):
        me = bpy.data.meshes.new('Wall')
        x = sgn * 3.2
        me.from_pydata([(x, -3, 0), (x, 14, 0), (x, 14, 8), (x, -3, 8)], [], [(0, 1, 2, 3)])
        uvl = me.uv_layers.new(); [setattr(uvl.data[i], 'uv', (me.vertices[me.loops[i].vertex_index].co.y, me.vertices[me.loops[i].vertex_index].co.z)) for i in range(len(me.loops))]
        w = bpy.data.objects.new('Wall_' + ('R' if sgn > 0 else 'L'), me); ALLEY.objects.link(w); me.materials.append(wall)
    me = bpy.data.meshes.new('BackWall'); me.from_pydata([(-4, 14, 0), (4, 14, 0), (4, 14, 8), (-4, 14, 8)], [], [(0, 1, 2, 3)])
    uvl = me.uv_layers.new(); [setattr(uvl.data[i], 'uv', (me.vertices[me.loops[i].vertex_index].co.x, me.vertices[me.loops[i].vertex_index].co.z)) for i in range(len(me.loops))]
    w = bpy.data.objects.new('Wall_Back', me); ALLEY.objects.link(w); me.materials.append(wall)
    def neon(text, loc, rot, size, color, strength=12, ext=0.012):
        cu = bpy.data.curves.new(text, 'FONT'); cu.body = text; cu.size = size
        cu.align_x = 'CENTER'; cu.align_y = 'CENTER'; cu.extrude = ext; cu.bevel_depth = 0.004
        o = bpy.data.objects.new('Neon_' + text.replace(' ', '_').replace("'", ''), cu)
        o.location = loc; o.rotation_euler = rot; ALLEY.objects.link(o)
        cu.materials.append(emission_mat('Neon_' + text, color, strength))
        return o
    neon('NOIR', (-3.15, 2.2, 3.1), (radians(90), 0, radians(90)), 0.75, (0.05, 0.45, 1.0), 4.5)
    neon('INVESTIGATIONS', (-3.15, 2.2, 2.55), (radians(90), radians(-6), radians(90)), 0.28, (0.05, 0.45, 1.0), 4.5)
    neon('JAZZ BAR', (3.15, 4.0, 3.2), (radians(90), radians(8), radians(-90)), 0.7, (0.7, 0.12, 1.0), 5)
    neon("SLIM'S PI", (3.15, 7.5, 2.4), (radians(90), 0, radians(-90)), 0.5, (0.85, 0.15, 1.0), 4)
    s = neon('SLIMS', (-3.1, 6.5, 3.4), (radians(90), radians(-90), radians(90)), 0.45, (1.0, 0.38, 0.04), 5)
    neon('HOTEL', (1.2, 13.9, 4.6), (radians(90), 0, 0), 0.9, (1.0, 0.1, 0.3), 3.5)
    neon('OPEN', (-1.6, 13.9, 2.0), (radians(90), 0, 0), 0.5, (0.05, 1.0, 0.65), 3.5)
build_env()

def build_fog():
    me = bpy.data.meshes.new('Fog')
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new('Fog_Volume', me); o.scale = (6.2, 18, 7); o.location = (0, 5, 3.4); ALLEY.objects.link(o)
    m = bpy.data.materials.new('Fog_Volume'); nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_MATERIAL': nt.nodes.remove(n)
    out = [n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'][0]
    pv = nt.nodes.new('ShaderNodeVolumePrincipled'); pv.inputs['Density'].default_value = 0.007
    set_in(pv, 'Anisotropy', 0.45); set_in(pv, 'Color', (0.8, 0.85, 1.0, 1))
    nt.links.new(pv.outputs[0], out.inputs['Volume'])
    me.materials.append(m)
build_fog()


def area(name, loc, target, color, energy, size=1.5, size_y=None):
    ld = bpy.data.lights.new(name, 'AREA'); ld.energy = energy; ld.color = color
    ld.shape = 'RECTANGLE' if size_y else 'SQUARE'; ld.size = size
    if size_y: ld.size_y = size_y
    o = bpy.data.objects.new(name, ld); o.location = loc; LIGHTS.objects.link(o)
    d = (Vector(target) - Vector(loc)); o.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    return o
area('Key_Warm', (-1.6, -2.6, 3.1), (0, 0, 1.4), (1.0, 0.86, 0.72), 260, 1.2)
area('Fill_Cyan', (2.4, -2.6, 1.8), (0, 0, 1.2), (0.35, 0.65, 1.0), 120, 2.0)
area('Rim_Magenta', (2.8, 1.8, 2.4), (0, 0, 1.3), (0.85, 0.3, 1.0), 650, 1.2, 3.0)
area('Rim_Orange', (-2.4, 2.6, 2.6), (0, 0, 1.4), (1.0, 0.48, 0.15), 450, 1.0, 2.5)
area('Rim_Blue_Top', (-1.0, 1.5, 3.6), (0, 0, 1.6), (0.3, 0.55, 1.0), 220, 1.5)
sd = bpy.data.lights.new('Face_Key', 'SPOT'); sd.energy = 45; sd.color = (0.88, 0.92, 1.0)
sd.spot_size = radians(24); sd.spot_blend = 0.6; sd.shadow_soft_size = 0.15
so = bpy.data.objects.new('Face_Key', sd); so.location = (0.7, -1.3, 2.15); LIGHTS.objects.link(so)
so.rotation_euler = (V(0, -0.02, 1.70) - so.location).to_track_quat('-Z', 'Y').to_euler()

def uv_all():
    try:
        for o in bpy.context.view_layer.objects: o.select_set(False)
        meshes = [o for o in CHAR.objects if o.type == 'MESH']
        for o in meshes: o.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=radians(66), island_margin=0.004)
        bpy.ops.object.mode_set(mode='OBJECT')
        for o in meshes: o.select_set(False)
        print('UV OK', len(meshes))
    except Exception as e:
        print('UV FAIL', e)
uv_all()

world = bpy.data.worlds.new('NoirNight'); scene.world = world
try: world.use_nodes = True
except Exception: pass
bg = world.node_tree.nodes.get('Background')
if bg: bg.inputs['Color'].default_value = (0.006, 0.008, 0.016, 1); bg.inputs['Strength'].default_value = 1.0

# report
tris = 0
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
for o in CHAR.all_objects:
    if o.type == 'MESH':
        me = o.evaluated_get(dg).to_mesh()
        t = sum(len(p.vertices) - 2 for p in me.polygons); tris += t
        print('  %-28s %7d tris' % (o.name, t))
        o.evaluated_get(dg).to_mesh_clear()
print('TOTAL CHARACTER TRIS:', tris)
path = os.path.join(OUT, 'Vince_Slim_Walker.blend')
bpy.ops.wm.save_as_mainfile(filepath=path)
print('SAVED', path)
