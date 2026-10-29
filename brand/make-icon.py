from PIL import Image, ImageDraw
import sys
S=1024
def bez(p0,p1,p2,p3,n=40):
    return [tuple((1-t)**3*a+3*(1-t)**2*t*b+3*(1-t)*t**2*c+t**3*d for a,b,c,d in zip(p0,p1,p2,p3)) for t in [i/n for i in range(n+1)]]
def shield():
    pts=[(32,0),(62,11),(62,36)]
    pts+=bez((62,36),(62,56),(48,70),(32,78))[1:]
    pts+=bez((32,78),(16,70),(2,56),(2,36))[1:]
    pts+=[(2,11)]
    return pts
def flame():
    p=[(32,18)]
    p+=bez((32,18),(40,28),(46,34),(46,44))[1:]
    p+=bez((46,44),(46,53),(40,59),(32,59))[1:]
    p+=bez((32,59),(24,59),(18,53),(18,44))[1:]
    p+=bez((18,44),(18,38),(22,34),(25,30))[1:]
    p+=bez((25,30),(26,35),(29,37),(31,37))[1:]
    p+=bez((31,37),(30,30),(30,24),(32,18))[1:]
    return p
def render(color_top,color_bot,bg=(14,19,23,255)):
    im=Image.new("RGBA",(S,S),(0,0,0,0))
    d=ImageDraw.Draw(im)
    d.rounded_rectangle((0,0,S-1,S-1),radius=int(S*0.22),fill=bg)
    sc=S*0.68/80; ox=(S-64*sc)/2; oy=(S-80*sc)/2+S*0.01
    tf=lambda pts:[(ox+(x-1)*sc*1.0+0,oy+(y)*sc) for x,y in pts]
    # gradient layer
    grad=Image.new("RGBA",(S,S))
    gd=ImageDraw.Draw(grad)
    for y in range(S):
        t=min(max((y-oy)/(80*sc),0),1)
        c=tuple(int(a+(b-a)*t) for a,b in zip(color_top,color_bot))+(255,)
        gd.line((0,y,S,y),fill=c)
    mask=Image.new("L",(S,S),0)
    md=ImageDraw.Draw(mask)
    sh=tf(shield())
    cx=sum(x for x,_ in sh)/len(sh); cy=sum(y for _,y in sh)/len(sh)
    md.polygon(sh,fill=255)
    inner=[(cx+(x-cx)*0.84,cy+(y-cy)*0.84) for x,y in sh]
    md.polygon(inner,fill=0)
    md.polygon(tf(flame()),fill=255)
    im.paste(grad,(0,0),mask)
    return im
def save(im,path):
    sizes=[16,24,32,48,64,128,256]
    im.save(path,format="ICO",sizes=[(s,s) for s in sizes])
out=sys.argv[1]
on=render((92,192,255),(31,120,224))
off=render((150,170,185),(100,120,135))
save(on,out+"/cindrixvpn.ico"); save(off,out+"/cindrixvpn-off.ico")
on.resize((512,512),Image.LANCZOS).save(out+"/cindrixvpn-icon-512.png")
