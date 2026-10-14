"""Builds the cue audio clips used in Unity (M53). Run from the Thesis/ project root:
    python Analysis/make_cue_clips.py
Needs numpy + ffmpeg. Source: Freesound #512471 "Electric zap" by michael_grinnell (CC0),
kept in ../Sources/. Outputs to Assets/Audio/: spark_zap_pulse.wav (110 ms, one pulse),
spark_zap_loop_1s.wav (zap x4, seamless 1 s loop), beep_matched.wav (beep at matched
loudness). All normalised to -10 LUFS with raised-cosine fades (no clicks)."""
import numpy as np, subprocess, wave, sys, re
SR=44100
def load(p, ac=2):
    raw=subprocess.run(["ffmpeg","-v","error","-i",p,"-f","f32le","-ac",str(ac),"-ar",str(SR),"-"],capture_output=True).stdout
    return np.frombuffer(raw,dtype=np.float32).reshape(-1,ac).copy()
def fade(x, fi_ms, fo_ms):
    n=len(x); fi=max(1,int(fi_ms*SR/1000)); fo=max(1,int(fo_ms*SR/1000))
    x[:fi]*= (0.5-0.5*np.cos(np.pi*np.arange(fi)/fi))[:,None]
    x[n-fo:]*= (0.5-0.5*np.cos(np.pi*np.arange(fo)[::-1]/fo))[:,None]
    return x
def write(p,x):
    x=np.clip(x,-1,1); w=wave.open(p,"wb"); w.setnchannels(x.shape[1]); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((x*32767).astype(np.int16).tobytes()); w.close()
def lufs(p):
    out=subprocess.run(["ffmpeg","-v","info","-stream_loop","20","-i",p,"-af","ebur128=peak=true","-f","null","-"],capture_output=True,text=True).stderr
    I=float(re.findall(r"I:\s+(-?[\d.]+) LUFS",out)[-1]); pk=float(re.findall(r"Peak:\s+(-?[\d.]+) dBFS",out)[-1]); return I,pk
TARGET=-10.0
zap=load("../Sources/512471__michael_grinnell__electric-zap.wav")
# pulse: first 110 ms, 2 ms in / 15 ms out
pulse=fade(zap[:int(0.110*SR)].copy(),2,15)
# loop: zap repeated 4x per second (225 ms zap + 25 ms gap), exactly 1.000 s, seamless
one=fade(zap.copy(),2,10); seg=np.zeros((int(0.250*SR),2),np.float32); seg[:len(one)]=one[:len(seg)]
loop=np.concatenate([seg]*4)[:SR]
beep=load("Assets/Audio/beep.wav")
beep=fade(beep,2,5)
for name,x in [("spark_zap_pulse",pulse),("spark_zap_loop_1s",loop),("beep_matched",beep)]:
    p=f"Assets/Audio/{name}.wav"; write(p,x); I,pk=lufs(p)
    g=10**((TARGET-I)/20); x2=x*g
    if np.abs(x2).max()>10**(-1/20): print(name,"would clip, limiting gain"); x2=x*(10**(-1/20)/np.abs(x).max())
    write(p,x2); I2,pk2=lufs(p)
    print(f"{name}: {len(x2)/SR*1000:.0f} ms, {I:.1f} -> {I2:.1f} LUFS, peak {pk2:.1f} dBFS, ends {abs(x2[0]).max():.4f}/{abs(x2[-1]).max():.4f}")
