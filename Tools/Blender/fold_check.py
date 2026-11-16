import bpy, math, json, sys
from shapely.geometry import MultiPoint, Polygon
flip_calf = "--flip" in sys.argv
bpy.ops.wm.open_mainfile(filepath="/home/claude/blender/out/ironclad_set.blend")
dg = bpy.context.evaluated_depsgraph_get()
def hull(name):
    o = bpy.data.objects[name].evaluated_get(dg)
    pts = [(v.co.z, -v.co.y, v.co.x) for v in o.data.vertices]  # (bone, outward, width)
    return pts
ENV = {"Thigh": (0.46, 0.22), "Calf": (0.42, 0.22)}
T=C=1.15; r=0.15; gap=0.06; inner=0.09; ankleH=0.26
def fitted(part, half, Ls):
    pts = hull(f"{part}{half}_Set01_Ironclad")
    W, D = ENV[part]
    bmin=min(p[0] for p in pts); bmax=max(p[0] for p in pts); omin=min(p[1] for p in pts); omax=max(p[1] for p in pts)
    wmin=min(p[2] for p in pts); wmax=max(p[2] for p in pts)
    sx = W/(wmax-wmin); sy = Ls/(bmax-bmin); sz = min((sx+sy)/2, 1.6*D/(omax-omin))
    out=[]
    for b,o,w in pts:
        y = -Ls/2 + (b-bmin)*sy
        z = inner + (o-omin)*sz
        out.append((y,z))
    return out
def place(pts, top, bot, sign):
    dy=top[0]-bot[0]; dz=top[1]-bot[1]; L=math.hypot(dy,dz); u=(dy/L,dz/L); n=(-u[1],u[0])
    if n[1]<0: n=(-n[0],-n[1])
    mid=((top[0]+bot[0])/2,(top[1]+bot[1])/2)
    return MultiPoint([(mid[0]+u[0]*a+n[0]*b*sign, mid[1]+u[1]*a+n[1]*b*sign) for a,b in pts]).convex_hull
def ik(H,A,fwd):
    import math
    dy=A[0]-H[0]; dz=A[1]-H[1]; d=math.hypot(dy,dz); base=math.atan2(dz,dy); a=math.acos((T*T+d*d-C*C)/(2*T*d)); t=base+(-a if fwd else a)
    return (H[0]+T*math.cos(t), H[1]+T*math.sin(t))
toe=Polygon([(0,0.2),(0,0.68),(0.25,0.68),(0.315,0.52),(0.315,0.32),(0.265,0.2)]); heel=Polygon([(0,-0.44),(0,-0.16),(0.25,-0.16),(0.25,-0.3),(0.22,-0.44)])
Ls = T - 2*(r+gap)
shapes = {}
for part in ("Thigh","Calf"):
    for half in ("Front","Back"):
        pts = fitted(part, half, Ls)
        if part=="Calf" and flip_calf: pts=[(-y,z) for y,z in pts]
        shapes[(part,half)] = pts
for name,fwd,e in [("Human",True,0.94),("ChickenFwd",True,0.80),("ReverseExt",False,0.93),("ChickenBack",False,0.74),("CritFold",False,0.66)]:
    A=(ankleH,0); H=(ankleH+e*2.3,0); K=ik(H,A,fwd)
    polys={}
    for half,s in (("Front",1),("Back",-1)):
        polys["th"+half]=place(shapes[("Thigh",half)],H,K,s)
        polys["ca"+half]=place(shapes[("Calf",half)],K,A,s)
    polys["toe"]=toe; polys["heel"]=heel
    hits=[]
    ks=list(polys)
    for i in range(len(ks)):
        for j in range(i+1,len(ks)):
            a,b=ks[i],ks[j]
            if a[:2]==b[:2] or (a in("toe","heel") and b in("toe","heel")): continue
            if polys[a].intersects(polys[b]) and polys[a].intersection(polys[b]).area>1e-6: hits.append(f"{a}-{b} area={polys[a].intersection(polys[b]).area:.5f} dist_to_knee={polys[a].intersection(polys[b]).centroid.distance(__import__("shapely.geometry").geometry.Point(K)):.3f}")
    print(name, hits or "clear")
