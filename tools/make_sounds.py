#!/usr/bin/env python3
"""Synthesises AVAYars's sound effects and tunes into ../src/AVAYars/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, saw, triangle and noise voices. The title
plays the Flight of the Bumblebee (Nikolai Rimsky-Korsakov, 1900), arranged for two voices; the game-over phrase is the
game's own. The Yar's buzz and the Neutral Zone's hum are loops that join up seamlessly. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAYars", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def line(bpm, notes, kind="pulse", mult=1.0, gain=0.55, release=0.6):
    """A tune as (note, beats), '-' a rest; mult transposes it."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note) * mult, kind=kind, release=release, gain=gain))
    return out


# ───────────────────────────── the loops ─────────────────────────────

def buzz(seconds=2.0):
    """The Yar's wings: a low saw buzz, beating thirty times a second. Whole cycles only, so the loop joins up."""
    n = int(RATE * seconds)
    out = []
    for i in range(n):
        t = i / RATE
        s = voice("saw", 110 * t) * 0.6 + voice("square", 55 * t) * 0.25
        beat = 0.55 + 0.45 * (0.5 + 0.5 * math.sin(2 * math.pi * 30 * t))
        out.append(s * beat)
    return out


def hum(seconds=2.0):
    """
    The Neutral Zone: a low electric hum under a scatter of square-wave blips that change thirty times a second, the
    way the zone's colours do.
    """
    n = int(RATE * seconds)
    step = RATE // 30
    out = [0.0] * n
    rng = random.Random(7)
    for k in range(0, n, step):
        f = rng.choice([220, 330, 440, 495, 587, 660, 880, 990])
        g = rng.uniform(0.1, 0.35)
        for i in range(step):
            if k + i < n:
                # Each blip fades in and out, so they don't click.
                env = math.sin(math.pi * i / step)
                out[k + i] += voice("square", f * i / RATE) * g * env
    for i in range(n):
        t = i / RATE
        out[i] += math.sin(2 * math.pi * 60 * t) * 0.5 + voice("tri", 120 * t) * 0.3 * (0.6 + 0.4 * math.sin(2 * math.pi * 3 * t))
    return out


# ───────────────────────────── the tunes ─────────────────────────────

def bumblebee():
    """The Flight of the Bumblebee (Rimsky-Korsakov, 1900): the chromatic fall, the buzzing theme on E, then on A, then home."""
    s = 0.25  # a semiquaver, in beats
    intro = ["E6", "D#6", "D6", "C#6", "C6", "B5", "A#5", "A5", "G#5", "G5", "F#5", "F5", "E5", "D#5", "D5", "C#5",
             "C5", "B4", "A#4", "A4", "G#4", "G4", "F#4", "F4"]
    on_e = ["E5", "D#5", "D5", "C#5", "D5", "C#5", "C5", "B4", "C5", "C#5", "D5", "D#5", "E5", "F5", "E5", "D#5"]
    on_a = ["A5", "G#5", "G5", "F#5", "G5", "F#5", "F5", "E5", "F5", "F#5", "G5", "G#5", "A5", "A#5", "A5", "G#5"]
    run = ["E5", "F5", "F#5", "G5", "G#5", "A5", "A#5", "B5", "C6", "B5", "A#5", "A5", "G#5", "G5", "F#5", "F5",
           "E5", "D#5", "D5", "C#5", "C5", "B4", "A#4", "B4"]
    lead = [(n, s) for n in intro + on_e * 2 + on_a * 2 + on_e * 2 + run] + [("A4", 2), ("-", 2)]
    e = 0.5

    def bar(low, high):
        return [(low, e), ("-", e), (high, e), ("-", e)]

    bass = [("E2", 1), ("-", 1)] * 3
    bass += (bar("A2", "E2") + bar("E2", "B2")) * 2
    bass += (bar("D3", "A2") + bar("A2", "E2")) * 2
    bass += (bar("A2", "E2") + bar("E2", "B2")) * 2
    bass += [("E2", 1), ("-", 1), ("E3", 1), ("-", 1), ("E2", 1), ("-", 1)] + [("A2", 2), ("-", 2)]
    assert sum(b for _, b in lead) == sum(b for _, b in bass)
    bpm = 138
    return mix(line(bpm, lead, "pulse", 1, 0.5, 0.3), line(bpm, bass, "tri", 1, 0.7, 0.4))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The Yar's energy missile; a cell shot to bits; a bite of the shield.
    write("Shoot.wav", tone(0.09, 1900, 500, "pulse", attack=0.001, release=0.6), 0.16)
    write("Hit.wav", mix(tone(0.12, 3000, 400, "noise", attack=0.001, release=1.2), tone(0.08, 700, 300, "square", release=0.8, gain=0.4)), 0.22)
    write("Eat.wav", cat(tone(0.04, 180, 120, "square", attack=0.001, release=0.5), tone(0.03, 1500, 900, "noise", release=1.0, gain=0.6),
                         tone(0.04, 150, 100, "square", release=0.7)), 0.24)
    # The Zorlon Cannon: called up, and fired.
    write("Summon.wav", cat(*[tone(0.05, hz(n), kind="square", release=0.4) for n in ["C4", "G4", "C5", "G5", "C6"]]), 0.22)
    write("Cannon.wav", mix(tone(0.6, 420, 60, "square", attack=0.002, release=0.8), tone(0.6, 2500, 200, "noise", release=1.5, gain=0.6)), 0.34)
    # The Swirl: spinning on its pad, then off.
    write("Swirl.wav", tone(0.6, 300, 900, "square", vibrato=0.25, vib_rate=22, release=0.4, gain=0.7), 0.2)
    write("Launch.wav", mix(tone(0.4, 200, 1400, "saw", attack=0.002, release=0.7), tone(0.4, 600, 4000, "noise", release=1.0, gain=0.5)), 0.26)
    # The Qotile destroyed: a long roar, rumbling down, crackling.
    boom = mix(tone(2.8, 1800, 120, "noise", attack=0.002, release=1.4),
               tone(2.8, 90, 30, "square", release=1.2, gain=0.7),
               cat(*[tone(0.05, RNG.uniform(200, 900), None, "noise", release=0.5, gain=RNG.uniform(0.3, 0.8)) for _ in range(30)]))
    write("Boom.wav", boom, 0.46)
    write("Die.wav", cat(tone(0.9, 900, 70, "square", vibrato=0.3, vib_rate=14, attack=0.002, release=0.6), tone(0.4, 300, 40, "noise", release=1.0, gain=0.6)), 0.32)
    write("ExtraLife.wav", cat(*[tone(0.06, hz(n), kind="square", release=0.5) for n in ["E5", "G5", "E6", "C6", "D6", "G6"]]), 0.24)
    # The loops under play.
    write("Buzz.wav", buzz(), 0.12)
    write("Hum.wav", hum(), 0.16)
    write("TitleTune.wav", cat(bumblebee(), silence(1.5)), 0.3)
    write("GameOver.wav", mix(line(80, [("E5", 0.5), ("D#5", 0.5), ("D5", 0.5), ("C#5", 0.5), ("C5", 1), ("B4", 1), ("A4", 3)], "pulse", 1, 0.5, 0.5),
                              line(80, [("A2", 2), ("E2", 2), ("A1", 3)], "tri", 1, 0.6, 0.3)), 0.4)


if __name__ == "__main__":
    main()
