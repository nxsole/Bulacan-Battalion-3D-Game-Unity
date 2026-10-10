import numpy as np, os
from PIL import Image
Image.MAX_IMAGE_PIXELS=None
base="Assets/R3DWorks/Free/URP_RiversidePlants/Trees/fbx"
out="Assets/MountainEnvironment/TropicalTrees"
brown=np.array([0.42,0.27,0.15],dtype=np.float32)
def recolor(src,dst,thumb=None):
    im=Image.open(src).convert("RGBA"); a=np.asarray(im).astype(np.float32)/255.
    rgb=a[...,:3]; al=a[...,3]
    mx=rgb.max(-1); mn=rgb.min(-1); sat=(mx-mn)/np.maximum(mx,1e-4)
    r,g,b=rgb[...,0],rgb[...,1],rgb[...,2]
    leaf=(g>r*1.02)&(g>b*1.05)&(sat>0.18)
    lum=0.3*r+0.59*g+0.11*b
    tone=np.clip(0.35+1.15*lum,0,1.2)[...,None]
    bark=np.clip(brown[None,None,:]*tone*1.25,0,1)
    res=np.where(leaf[...,None],rgb,bark)
    o=np.concatenate([res,al[...,None]],-1)
    Image.fromarray((np.clip(o,0,1)*255).astype(np.uint8),"RGBA").save(dst,compress_level=1)
    if thumb:
        bg=Image.new("RGB",(4096,4096),(255,0,255)); t=Image.open(dst); bg.paste(t,mask=t.split()[3]); bg.resize((900,900)).save(thumb)
for n in ["birch_big_002","birch_medium_001","birch_small_001"]:
    d=f"{base}/{n}/{n}.fbm"
    for suf in ["_Color","_Billboard_Color"]:
        s=f"{d}/{n}{suf}.png"
        if os.path.exists(s):
            if os.path.exists(f"{out}/Tropical_{n}{suf}.png"): continue
            recolor(s,f"{out}/Tropical_{n}{suf}.png","_trop_thumb.png" if (n=="birch_big_002" and suf=="_Color") else None)
            print("done",n,suf,flush=True)
