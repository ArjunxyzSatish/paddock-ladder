"""Writes the interface sounds. Standard library only, so it runs anywhere: python make_sounds.py <project folder>"""
import math
import pathlib
import random
import struct
import sys
import wave

SR = 44100
random.seed(1971)


def render(seconds, sample):
    return [sample(i / SR) for i in range(int(seconds * SR))]


def edge(t, length):
    """Removes clicks at each end of a sound."""
    return min(1.0, t / 0.0012, (length - t) / 0.004)


def tone(t, freq, decay):
    return math.sin(2 * math.pi * freq * t) * math.exp(-t * decay)


def hover():
    length = 0.03
    return render(length, lambda t: edge(t, length) * (tone(t, 2640, 170) + 0.28 * tone(t, 5280, 260)) * 0.2)


def click():
    length = 0.085
    def sample(t):
        snap = random.uniform(-1, 1) * math.exp(-t * 900) * 0.3
        return edge(t, length) * (tone(t, 1180, 62) * 0.62 + tone(t, 1760, 95) * 0.26 + snap) * 0.72
    return render(length, sample)


def scroll():
    length = 0.024
    return render(length, lambda t: edge(t, length) * (tone(t, 1560, 240) + 0.35 * tone(t, 3120, 340)) * 0.3)


def confirm():
    """Two rising notes in the key of the menu music, so a big step forward sounds like one."""
    length = 0.55
    notes = [(0.0, 698.46), (0.09, 1046.5)]
    def sample(t):
        total = 0.0
        for start, freq in notes:
            if t >= start:
                local = t - start
                total += tone(local, freq, 7.5) + 0.34 * tone(local, freq * 4, 26) + 0.1 * tone(local, freq * 2, 12)
        return edge(t, length) * total * 0.36
    return render(length, sample)


out = pathlib.Path(sys.argv[1]) / "sounds"
out.mkdir(parents=True, exist_ok=True)
for name, samples in [("hover", hover()), ("click", click()), ("scroll", scroll()), ("confirm", confirm())]:
    peak = max(abs(x) for x in samples)
    assert peak <= 0.98, (name, peak)
    with wave.open(str(out / (name + ".wav")), "wb") as target:
        target.setnchannels(1)
        target.setsampwidth(2)
        target.setframerate(SR)
        target.writeframes(b"".join(struct.pack("<h", int(x * 32767)) for x in samples))
    print(name, round(len(samples) / SR * 1000), "ms, peak", round(peak, 2))
