"""
Procedural sound set for the NightOffice greybox prototype.
Writes 48 kHz / 16-bit mono WAVs into Assets/_Project/Audio/Generated.

    python Tools/audio_synth.py

Every clip is synthesized (no recordings) so the prototype carries no third-party audio.
Replace any file with a real recording of the same name later; SfxLibrary keeps working.
"""
import os
import wave

import numpy as np
from scipy import signal

SR = 48000
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Assets", "_Project", "Audio", "Generated")
RNG = np.random.default_rng(20260928)


# ------------------------------------------------------------------ helpers
def t_axis(sec):
    return np.arange(int(sec * SR)) / SR


def noise(sec):
    return RNG.standard_normal(int(sec * SR))


def pink(sec):
    n = int(sec * SR)
    w = RNG.standard_normal(n)
    b = [0.049922035, -0.095993537, 0.050612699, -0.004408786]
    a = [1, -2.494956002, 2.017265875, -0.522189400]
    return signal.lfilter(b, a, w)


def sos(kind, freq, order=2):
    nyq = SR / 2
    if isinstance(freq, (list, tuple)):
        wn = [min(f / nyq, 0.999) for f in freq]
    else:
        wn = min(freq / nyq, 0.999)
    return signal.butter(order, wn, btype=kind, output="sos")


def lp(x, f, order=2):
    return signal.sosfilt(sos("lowpass", f, order), x)


def hp(x, f, order=2):
    return signal.sosfilt(sos("highpass", f, order), x)


def bp(x, lo, hi, order=2):
    return signal.sosfilt(sos("bandpass", [lo, hi], order), x)


def reson(x, f, bw):
    """2-pole resonator (formant/body mode)."""
    r = np.exp(-np.pi * bw / SR)
    theta = 2 * np.pi * f / SR
    a = [1, -2 * r * np.cos(theta), r * r]
    b = [1 - r]
    return signal.lfilter(b, a, x)


def env_exp(sec, tau, attack=0.002):
    t = t_axis(sec)
    e = np.exp(-t / max(tau, 1e-4))
    na = max(1, int(attack * SR))
    e[:na] *= np.linspace(0, 1, na)
    return e


def env_adsr(sec, a, d, s, r):
    n = int(sec * SR)
    e = np.zeros(n)
    na, nd, nr = int(a * SR), int(d * SR), int(r * SR)
    ns = max(0, n - na - nd - nr)
    idx = 0
    for seg in (np.linspace(0, 1, na, endpoint=False), np.linspace(1, s, nd, endpoint=False), np.full(ns, s), np.linspace(s, 0, nr)):
        e[idx: idx + len(seg)] = seg[: max(0, n - idx)]
        idx += len(seg)
    return e


def sine(f, sec, phase=0.0):
    return np.sin(2 * np.pi * f * t_axis(sec) + phase)


def fit(x, sec):
    n = int(sec * SR)
    if len(x) >= n:
        return x[:n]
    return np.concatenate([x, np.zeros(n - len(x))])


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


def at(x, start_sec, total_sec):
    out = np.zeros(int(total_sec * SR))
    s = int(start_sec * SR)
    e = min(len(out), s + len(x))
    if s < len(out):
        out[s:e] += x[: e - s]
    return out


def fade(x, fin=0.003, fout=0.01):
    x = x.copy()
    ni, no = int(fin * SR), int(fout * SR)
    if ni > 0:
        x[:ni] *= np.linspace(0, 1, ni)
    if no > 0:
        x[-no:] *= np.linspace(1, 0, no)
    return x


def peak_norm(x, db=-3.0):
    p = np.max(np.abs(x)) + 1e-12
    return x / p * (10 ** (db / 20))


def rms_norm(x, db):
    r = np.sqrt(np.mean(x ** 2)) + 1e-12
    y = x / r * (10 ** (db / 20))
    p = np.max(np.abs(y))
    if p > 0.95:
        y *= 0.95 / p
    return y


def loopify(x, xfade=0.8):
    """Make a seamless loop by crossfading the tail into the head."""
    n = int(xfade * SR)
    head, tail = x[:n], x[-n:]
    w = np.linspace(0, 1, n)
    blended = tail * (1 - w) + head * w
    return np.concatenate([blended, x[n:-n]])


def reverb(x, sec=1.2, decay=0.35, wet=0.35, lo=150, hi=4000):
    ir_n = noise(sec) * np.exp(-t_axis(sec) / decay)
    ir_n = bp(ir_n, lo, hi)
    ir_n /= np.sqrt(np.sum(ir_n ** 2)) + 1e-9
    w = signal.fftconvolve(x, ir_n)[: len(x) + int(sec * SR)]
    dry = np.concatenate([x, np.zeros(len(w) - len(x))])
    return dry * (1 - wet) + w * wet


def write(name, x):
    os.makedirs(OUT, exist_ok=True)
    x = np.clip(x, -1, 1)
    pcm = (x * 32767).astype(np.int16)
    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"{name:28s} {len(x) / SR:5.2f}s")


# ------------------------------------------------------------------ ambience loops
def amb_fan():
    sec = 13.0
    t = t_axis(sec)
    n = pink(sec)
    body = lp(n, 900, 4) * 0.8 + reson(n, 175, 40) * 3.0 + reson(n, 350, 60) * 1.6
    hum = 0.05 * np.sin(2 * np.pi * 60 * t) + 0.03 * np.sin(2 * np.pi * 120 * t)
    wob = 1 + 0.035 * np.sin(2 * np.pi * 0.31 * t) + 0.02 * np.sin(2 * np.pi * 0.07 * t + 1)
    x = (body / np.std(body) * 0.2 + hum) * wob
    write("amb_fan", rms_norm(loopify(x), -23))


def amb_fluorescent():
    sec = 5.0
    t = t_axis(sec)
    x = np.zeros_like(t)
    for k, a in zip(range(1, 12), [1, 0.55, 0.35, 0.3, 0.18, 0.15, 0.1, 0.08, 0.06, 0.05, 0.04]):
        x += a * np.sin(2 * np.pi * 120 * k * t + RNG.uniform(0, 6.28))
    buzz = np.sign(np.sin(2 * np.pi * 120 * t)) * 0.3
    buzz = bp(buzz, 1500, 5000) * 0.25
    x = x * (1 + 0.04 * np.sin(2 * np.pi * 0.9 * t)) + buzz + hp(noise(sec), 3000) * 0.004
    write("amb_fluorescent", rms_norm(loopify(x, 0.5), -30))


def amb_room():
    sec = 12.0
    t = t_axis(sec)
    x = lp(pink(sec), 180, 4) * 3 + 0.02 * np.sin(2 * np.pi * 60 * t)
    x *= 1 + 0.08 * np.sin(2 * np.pi * 0.05 * t)
    write("amb_room", rms_norm(loopify(x, 1.0), -36))


def amb_substation():
    sec = 9.0
    t = t_axis(sec)
    x = (np.sin(2 * np.pi * 120 * t) + 0.55 * np.sin(2 * np.pi * 240 * t) + 0.3 * np.sin(2 * np.pi * 360 * t)
         + 0.15 * np.sin(2 * np.pi * 480 * t) + 0.2 * np.sin(2 * np.pi * 60 * t))
    x *= 1 + 0.03 * np.sin(2 * np.pi * 0.23 * t)
    x += lp(pink(sec), 400) * 0.3
    write("amb_substation", rms_norm(loopify(x, 0.6), -25))


def amb_stair():
    sec = 12.0
    t = t_axis(sec)
    n = bp(pink(sec), 250, 1800) * 2
    swell = 0.6 + 0.4 * (0.5 + 0.5 * np.sin(2 * np.pi * 0.08 * t)) * (0.5 + 0.5 * np.sin(2 * np.pi * 0.19 * t + 2))
    x = n * swell + lp(pink(sec), 120) * 2
    write("amb_stair", rms_norm(loopify(x, 1.2), -37))


def elevator_motor():
    sec = 4.0
    t = t_axis(sec)
    f = 410 + 6 * np.sin(2 * np.pi * 0.7 * t)
    ph = 2 * np.pi * np.cumsum(f) / SR
    whine = np.sin(ph) * 0.3 + np.sin(2 * ph) * 0.12 + np.sin(3 * ph) * 0.05
    rumble = lp(pink(sec), 160, 4) * 2.5
    x = whine + rumble + bp(noise(sec), 800, 2500) * 0.02
    write("elevator_motor", rms_norm(loopify(x, 0.4), -27))


def radio_hiss():
    sec = 4.0
    x = bp(noise(sec), 450, 3000, 3)
    crackle = (RNG.random(int(sec * SR)) < 0.0008) * RNG.standard_normal(int(sec * SR)) * 6
    x = x + bp(crackle, 800, 4000)
    write("radio_hiss", rms_norm(loopify(x, 0.3), -30))


def radio_dead():
    sec = 3.0
    t = t_axis(sec)
    x = bp(noise(sec), 300, 4200, 2)
    am = 0.5 + 0.5 * np.abs(signal.sosfilt(sos("lowpass", 18), noise(sec)) * 8)
    crackle = (RNG.random(len(t)) < 0.003) * RNG.standard_normal(len(t)) * 5
    x = x * np.clip(am, 0.2, 1.4) + bp(crackle, 1000, 6000)
    write("radio_dead", rms_norm(loopify(x, 0.25), -22))


# ------------------------------------------------------------------ footsteps
def step(name, variants, thump_f, thump_tau, click_band, click_gain, scuff_gain, ring=None, sec=0.3, lowpass=None, tail=None):
    for i in range(variants):
        jf = RNG.uniform(0.9, 1.12)
        th = sine(thump_f * jf, sec) * env_exp(sec, thump_tau) * 1.0
        cl = bp(noise(sec), click_band[0] * jf, click_band[1]) * env_exp(sec, 0.012) * click_gain
        sc = bp(noise(sec), 200, 900) * env_adsr(sec, 0.01, 0.06, 0.2, 0.08) * scuff_gain
        x = th + cl + sc
        if ring:
            x += reson(noise(sec) * env_exp(sec, 0.004), ring * jf, 25) * 0.6
        if lowpass:
            x = lp(x, lowpass, 2)
        if tail:
            x = reverb(x, *tail)
        write(f"{name}_{i + 1}", peak_norm(fade(x, 0.001, 0.02), -3))


def footsteps():
    step("step_concrete", 4, 95, 0.045, (1200, 4500), 0.7, 0.25)
    step("step_stair", 3, 120, 0.05, (1500, 5000), 0.6, 0.2, ring=720)
    step("step_heavy", 3, 70, 0.09, (700, 2500), 0.5, 0.5, sec=0.45)
    step("step_follower", 3, 85, 0.06, (900, 3000), 0.55, 0.35, lowpass=2200)
    step("step_echo", 2, 110, 0.05, (1200, 4000), 0.5, 0.25, ring=700, sec=0.35, tail=(1.4, 0.45, 0.75, 200, 3000))


# ------------------------------------------------------------------ doors & cards
def latch(sec=0.12, f=2600):
    return reson(noise(sec) * env_exp(sec, 0.002), f, 180) * 0.9 + bp(noise(sec), 2000, 7000) * env_exp(sec, 0.004) * 0.5


def door_sounds():
    sec = 0.8
    creak_f = 330 + 120 * np.sin(np.linspace(0, 2.2, int(0.45 * SR)))
    ph = 2 * np.pi * np.cumsum(creak_f) / SR
    creak = (np.sign(np.sin(ph)) * 0.2) * env_adsr(0.45, 0.05, 0.1, 0.6, 0.2)
    creak = bp(creak, 250, 1800) * 0.4
    write("door_open", peak_norm(fade(mix(at(latch(), 0, sec), at(creak, 0.08, sec))), -3))
    thud = sine(110, 0.5) * env_exp(0.5, 0.07) + bp(noise(0.5), 180, 1100) * env_exp(0.5, 0.03) * 0.6
    write("door_close", peak_norm(fade(mix(at(thud, 0, 0.6), at(latch(), 0.005, 0.6))), -3))

    # unit doors: lighter
    write("unit_door_open", peak_norm(fade(mix(at(latch(f=3100), 0, 0.7), at(creak * 0.6, 0.06, 0.7))), -4))
    thud2 = sine(140, 0.4) * env_exp(0.4, 0.05) + bp(noise(0.4), 250, 1500) * env_exp(0.4, 0.02) * 0.5
    write("unit_door_close", peak_norm(fade(mix(at(thud2, 0, 0.5), at(latch(f=3100), 0.003, 0.5))), -4))

    # fire door: heavy metal
    def metal(sec, partials, tau):
        x = np.zeros(int(sec * SR))
        for f, a in partials:
            x += a * sine(f, sec, RNG.uniform(0, 6)) * env_exp(sec, tau * RNG.uniform(0.7, 1.2))
        return x

    clank = metal(0.6, [(520, 1), (1310, 0.6), (2150, 0.4), (3020, 0.2)], 0.12) + latch(0.6, 1800) * 0.8
    push = bp(noise(0.3), 400, 2500) * env_exp(0.3, 0.02)
    write("fire_door_open", peak_norm(fade(mix(at(push, 0, 0.9), at(clank, 0.04, 0.9))), -3))
    boom = sine(62, 1.4) * env_exp(1.4, 0.18) * 1.2 + lp(noise(1.4), 250) * env_exp(1.4, 0.08) * 1.5
    ring = metal(1.4, [(210, 0.5), (487, 0.7), (933, 0.5), (1544, 0.35), (2270, 0.2)], 0.35)
    rattle = bp(noise(0.3), 1500, 5000) * (np.sin(2 * np.pi * 38 * t_axis(0.3)) > 0.3) * env_exp(0.3, 0.08) * 0.4
    write("fire_door_slam", peak_norm(fade(reverb(mix(boom, ring, at(rattle, 0.03, 1.4), at(latch(0.2, 2000), 0, 1.4)), 0.9, 0.3, 0.25)), -1))

    clicks = np.zeros(int(0.4 * SR))
    for s in (0.0, 0.07, 0.15, 0.22):
        clicks += at(latch(0.08, RNG.uniform(1900, 2800)) * RNG.uniform(0.6, 1.0), s, 0.4)
    write("door_locked", peak_norm(fade(clicks), -4))

    # knock: knuckle on a hollow wooden door
    for i in range(3):
        sec = 0.3
        exc = noise(sec) * env_exp(sec, 0.0015)
        body = reson(exc, 175 * RNG.uniform(0.95, 1.05), 30) * 3 + reson(exc, 320 * RNG.uniform(0.95, 1.05), 45) * 1.5 + reson(exc, 610, 80) * 0.6
        click = bp(noise(sec), 1500, 5000) * env_exp(sec, 0.003) * 0.3
        write(f"knock_{i + 1}", peak_norm(fade(body + click, 0.0005, 0.03), -1))

    # door rattle (slow knob turn)
    sec = 1.6
    x = np.zeros(int(sec * SR))
    for k in range(9):
        x += at(latch(0.1, RNG.uniform(1600, 2400)) * RNG.uniform(0.2, 0.5), 0.12 + k * 0.14 + RNG.uniform(-0.02, 0.02), sec)
    squeak_f = 900 + 200 * np.linspace(0, 1, int(1.2 * SR))
    squeak = np.sin(2 * np.pi * np.cumsum(squeak_f) / SR) * env_adsr(1.2, 0.2, 0.3, 0.4, 0.4) * 0.08
    write("door_rattle", peak_norm(fade(mix(x, at(squeak, 0.2, sec))), -6))

    beep = sine(1850, 0.09) * env_adsr(0.09, 0.003, 0.02, 0.8, 0.02)
    write("card_ok", peak_norm(fade(mix(at(latch(0.05, 3500) * 0.3, 0, 0.12), at(beep, 0.01, 0.12))), -6))
    lo = sine(420, 0.1) * env_adsr(0.1, 0.004, 0.02, 0.8, 0.02) + sine(840, 0.1) * 0.2
    write("card_deny", peak_norm(fade(mix(at(lo, 0, 0.3), at(lo, 0.16, 0.3))), -6))


# ------------------------------------------------------------------ radio
def radio_sounds():
    click = bp(noise(0.05), 1000, 6000) * env_exp(0.05, 0.002)
    burst = bp(noise(0.09), 700, 3500) * env_adsr(0.09, 0.002, 0.03, 0.4, 0.03) * 0.5
    write("radio_keyup", peak_norm(fade(mix(at(click, 0, 0.12), at(burst, 0.01, 0.12))), -8))
    write("radio_release", peak_norm(fade(mix(at(click, 0, 0.08), at(click * 0.5, 0.025, 0.08))), -9))

    # 치직: noise burst through a sweeping band with crackle
    sec = 0.34
    n = noise(sec)
    t = t_axis(sec)
    out = np.zeros_like(n)
    blocks = 34
    for b in range(blocks):
        s, e = b * len(n) // blocks, (b + 1) * len(n) // blocks
        c = 3200 - 2200 * (b / blocks)
        out[s:e] = bp(n[max(0, s - 400):e], c * 0.55, min(c * 1.4, 20000), 2)[-(e - s):]
    crack = (RNG.random(len(n)) < 0.02) * RNG.standard_normal(len(n)) * 3
    x = (out + bp(crack, 1500, 6000)) * env_adsr(sec, 0.004, 0.06, 0.7, 0.12)
    write("radio_squelch", peak_norm(fade(x), -3))

    tone = sine(1000, 0.08) * env_adsr(0.08, 0.004, 0.01, 0.9, 0.01)
    busy = mix(at(tone, 0, 0.5), at(tone, 0.14, 0.5), at(tone, 0.28, 0.5))
    write("radio_busy", peak_norm(fade(busy), -6))
    write("radio_rx_open", peak_norm(fade(mix(at(click, 0, 0.1), at(bp(noise(0.07), 900, 3500) * env_exp(0.07, 0.02), 0.005, 0.1))), -8))


# ------------------------------------------------------------------ elevator
def elevator_sounds():
    sec = 1.6
    x = np.zeros(int(sec * SR))
    for f, a, tau in [(1046, 1.0, 0.5), (2093, 0.3, 0.3), (2865, 0.2, 0.2), (3920, 0.08, 0.15)]:
        x += a * sine(f, sec) * env_exp(sec, tau, 0.004)
    write("elevator_ding", peak_norm(fade(x, 0.002, 0.2), -4))

    sec = 1.5
    slide = lp(pink(sec), 500, 2) * env_adsr(sec, 0.15, 0.2, 0.7, 0.35) * 3
    slide += bp(noise(sec), 1500, 4000) * env_adsr(sec, 0.1, 0.2, 0.3, 0.3) * 0.05
    thump = sine(90, 0.3) * env_exp(0.3, 0.05)
    write("elevator_door", peak_norm(fade(mix(slide, at(thump, 1.2, sec))), -4))

    write("elevator_button", peak_norm(fade(mix(at(latch(0.05, 3000) * 0.5, 0, 0.12), at(sine(2600, 0.05) * env_exp(0.05, 0.015) * 0.2, 0.005, 0.12))), -8))
    jolt = sine(55, 0.5) * env_exp(0.5, 0.1) + bp(noise(0.5), 300, 2500) * env_exp(0.5, 0.06) * 0.4
    write("elevator_jolt", peak_norm(fade(jolt), -3))


# ------------------------------------------------------------------ office
def office_sounds():
    sec = 6.5
    parts = []
    parts.append(at(sine(2100, 1.0) * 0.25 * env_adsr(1.0, 0.02, 0.1, 1, 0.05), 0.0, sec))
    for k in range(10):
        f = RNG.choice([1200, 1800, 2400, 980, 1650])
        parts.append(at(sine(f, 0.09) * 0.18, 1.05 + k * 0.1, sec))
    parts.append(at(bp(noise(0.8), 800, 3500) * 0.12, 2.1, sec))
    motor_t = t_axis(3.2)
    motor = (np.sign(np.sin(2 * np.pi * 310 * motor_t)) * 0.08) * (0.6 + 0.4 * np.sign(np.sin(2 * np.pi * 8 * motor_t)))
    motor = bp(motor, 200, 3000) + bp(noise(3.2), 1000, 5000) * 0.03
    parts.append(at(motor * env_adsr(3.2, 0.05, 0.1, 1, 0.3), 3.0, sec))
    parts.append(at(latch(0.2, 1500) * 0.4, 6.2, sec))
    write("fax_print", peak_norm(fade(mix(*parts), 0.005, 0.1), -6))

    write("terminal_click", peak_norm(fade(latch(0.04, 4000)), -12))
    chime = mix(at(sine(880, 0.25) * env_exp(0.25, 0.08), 0, 0.5), at(sine(1320, 0.3) * env_exp(0.3, 0.1), 0.12, 0.5))
    write("terminal_alert", peak_norm(fade(chime), -8))

    sec = 1.8
    f = np.linspace(120, 25, int(sec * SR))
    hum = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_adsr(sec, 0.01, 0.3, 0.6, 1.2)
    write("power_down", peak_norm(fade(mix(at(latch(0.2, 900) * 1.5, 0, sec), hum * 0.7)), -3))
    f = np.linspace(30, 120, int(1.5 * SR))
    hum = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_adsr(1.5, 0.6, 0.2, 0.7, 0.3)
    write("power_up", peak_norm(fade(mix(at(latch(0.2, 900) * 1.5, 0, 1.5), hum * 0.6)), -4))


# ------------------------------------------------------------------ lights & electric
def light_sounds():
    sec = 0.6
    pop = mix(bp(noise(0.08), 400, 6000) * env_exp(0.08, 0.008) * 1.2, sine(140, 0.1) * env_exp(0.1, 0.02))
    fizz = bp(noise(0.5), 2000, 8000) * (RNG.random(int(0.5 * SR)) < 0.05) * env_exp(0.5, 0.15) * 3
    write("light_pop", peak_norm(fade(lp(mix(at(pop, 0, sec), at(fizz, 0.03, sec)), 6000, 4)), -3))
    sec = 0.7
    x = np.zeros(int(sec * SR))
    for s in (0.0, 0.12, 0.2, 0.41):
        tk = mix(bp(noise(0.05), 2500, 7000) * env_exp(0.05, 0.004), sine(120, 0.05) * env_exp(0.05, 0.01) * 0.3)
        x += at(tk, s, sec)
    write("light_flicker", peak_norm(fade(x), -8))
    sec = 1.4
    clunk = mix(sine(80, 0.4) * env_exp(0.4, 0.05), latch(0.3, 1400) * 1.2)
    t = t_axis(0.9)
    humon = np.sin(2 * np.pi * 120 * t) * env_adsr(0.9, 0.3, 0.2, 0.5, 0.3) * 0.2
    write("panel_reset", peak_norm(fade(mix(at(clunk, 0, sec), at(clunk * 0.7, 0.35, sec), at(humon, 0.45, sec))), -3))
    write("flashlight_click", peak_norm(fade(mix(at(latch(0.05, 3800), 0, 0.08), at(latch(0.04, 2400) * 0.5, 0.02, 0.08))), -10))
    sec = 0.9
    x = np.zeros(int(sec * SR))
    for k, (s, a) in enumerate([(0, 1), (0.16, 0.55), (0.27, 0.35), (0.34, 0.2), (0.39, 0.12)]):
        hit = mix(reson(noise(0.12) * env_exp(0.12, 0.002), RNG.uniform(1800, 3200), 150) * a, sine(300, 0.1) * env_exp(0.1, 0.01) * a * 0.4)
        x += at(hit, s, sec)
    roll = bp(noise(0.35), 1500, 5000) * env_adsr(0.35, 0.05, 0.1, 0.4, 0.2) * 0.1
    write("flashlight_drop", peak_norm(fade(mix(x, at(roll, 0.4, sec))), -3))


# ------------------------------------------------------------------ entities
def breath(sec_in, sec_out, gap, lo, hi, close):
    inh = bp(pink(sec_in), lo, hi) * env_adsr(sec_in, sec_in * 0.6, 0.1, 0.8, sec_in * 0.3)
    exh = bp(pink(sec_out), lo * 0.7, hi * 0.7) * env_adsr(sec_out, 0.08, sec_out * 0.3, 0.6, sec_out * 0.55)
    total = sec_in + gap + sec_out + 0.2
    x = mix(at(inh * 0.8, 0, total), at(exh, sec_in + gap, total))
    if close:
        x += at(bp(noise(0.03), 2000, 6000) * env_exp(0.03, 0.005) * 0.3, sec_in + gap * 0.5, total)
        x = x + lp(x, 300) * 0.8
    return x


def entity_sounds():
    write("breath_close", peak_norm(fade(mix(breath(1.1, 1.4, 0.25, 500, 2600, False), at(breath(1.0, 1.3, 0.2, 500, 2600, False), 3.1, 5.8))), -4))
    write("breath_ear", peak_norm(fade(breath(0.9, 1.1, 0.15, 700, 4200, True)), -2))

    sec = 1.1
    x = np.zeros(int(sec * SR))
    times = np.sort(RNG.uniform(0.05, 0.75, 11))
    for s in times:
        c = bp(noise(0.02), 1200, 6500) * env_exp(0.02, 0.0015) * RNG.uniform(0.4, 1.0)
        x += at(c, s, sec)
    for s in (0.3, 0.62):
        x += at(mix(sine(160, 0.08) * env_exp(0.08, 0.012) * 0.8, reson(noise(0.08) * env_exp(0.08, 0.002), 900, 120) * 0.6), s, sec)
    write("neck_crack", peak_norm(fade(reverb(x, 0.5, 0.15, 0.2)), -2))

    sec = 3.5
    t = t_axis(sec)
    boom = np.sin(2 * np.pi * np.cumsum(np.linspace(70, 28, len(t))) / SR) * env_exp(sec, 0.9)
    swell = bp(noise(sec), 200, 3000) * np.linspace(0, 1, len(t)) ** 3 * 0.4
    swell = swell[::-1]
    ringing = sine(311, sec) * env_exp(sec, 1.2) * 0.12 + sine(466, sec) * env_exp(sec, 0.9) * 0.08
    write("vanish_sting", peak_norm(fade(mix(boom, swell, ringing), 0.001, 0.4), -1))


# ------------------------------------------------------------------ speech-like babble (formant synthesis)
VOWELS = {
    "a": (800, 1250, 2550), "eo": (590, 1000, 2500), "o": (450, 800, 2500), "u": (340, 820, 2300),
    "eu": (360, 1420, 2500), "i": (290, 2250, 3000), "e": (460, 1880, 2600),
}


def babble(sec, f0_base, seed, pause_range=(0.25, 0.65)):
    rng = np.random.default_rng(seed)
    out = np.zeros(int(sec * SR))
    pos = 0.15
    while pos < sec - 0.4:
        n_syl = rng.integers(3, 9)
        phrase_f0 = f0_base * rng.uniform(0.95, 1.15)
        for k in range(n_syl):
            syl = rng.uniform(0.11, 0.24)
            if pos + syl > sec - 0.1:
                break
            decl = 1 - 0.25 * k / max(1, n_syl - 1)
            f0 = phrase_f0 * decl * (1 + 0.06 * rng.standard_normal())
            vowel = VOWELS[rng.choice(list(VOWELS.keys()))]
            t = t_axis(syl)
            f0c = f0 * (1 + 0.05 * np.sin(2 * np.pi * rng.uniform(2, 5) * t))
            ph = 2 * np.pi * np.cumsum(f0c) / SR
            src = signal.sawtooth(ph) + 0.05 * rng.standard_normal(len(t))
            src = lp(src, 1400, 1)
            v = (reson(src, vowel[0], 80) * 1.0 + reson(src, vowel[1], 110) * 0.55 + reson(src, vowel[2], 160) * 0.25)
            v *= env_adsr(syl, 0.025, 0.04, 0.85, 0.05)
            seg = v
            if rng.random() < 0.55:
                cons_len = rng.uniform(0.02, 0.07)
                kind = rng.integers(0, 3)
                c = noise(cons_len)
                c = hp(c, 3500) if kind == 0 else bp(c, 1500, 4500) if kind == 1 else bp(c, 400, 1800)
                c *= env_adsr(cons_len, 0.005, 0.01, 0.6, 0.01) * (0.25 if kind == 0 else 0.35)
                seg = np.concatenate([c, seg])
            end = min(len(out), int(pos * SR) + len(seg))
            out[int(pos * SR):end] += seg[: end - int(pos * SR)]
            pos += len(seg) / SR + rng.uniform(0.0, 0.04)
        pos += rng.uniform(*pause_range)
    return out


def voices():
    write("mumble", peak_norm(fade(babble(7.0, 118, 7), 0.01, 0.2), -3))
    x = babble(12.0, 132, 11)
    write("test_voice", peak_norm(fade(x, 0.01, 0.3), -3))


# ------------------------------------------------------------------ ui
def ui_sounds():
    write("ui_click", peak_norm(fade(latch(0.03, 5000)), -14))
    tone = mix(at(sine(660, 0.15) * env_exp(0.15, 0.06), 0, 0.35), at(sine(990, 0.2) * env_exp(0.2, 0.08), 0.09, 0.35))
    write("complaint_done", peak_norm(fade(tone), -10))


if __name__ == "__main__":
    amb_fan()
    amb_fluorescent()
    amb_room()
    amb_substation()
    amb_stair()
    elevator_motor()
    radio_hiss()
    radio_dead()
    footsteps()
    door_sounds()
    radio_sounds()
    elevator_sounds()
    office_sounds()
    light_sounds()
    entity_sounds()
    voices()
    ui_sounds()
    print("done ->", OUT)
