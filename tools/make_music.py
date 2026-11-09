"""Renders Paddock Ladder's original menu track: relaxed bossa lounge, fully synthesised here."""
import pathlib
import sys
import wave

import lameenc
import numpy as np

SR = 44100
BPM = 126.0
BEAT = 60.0 / BPM
BAR = 4 * BEAT
rng = np.random.default_rng(1971)

QUALITIES = {
    "maj9": [4, 7, 11, 14],
    "m9": [3, 7, 10, 14],
    "m7": [3, 7, 10, 15],
    "9": [4, 7, 10, 14],
    "13": [4, 10, 14, 21],
}
ROOTS = {"C": 0, "Db": 1, "D": 2, "Eb": 3, "E": 4, "F": 5, "Gb": 6, "G": 7, "Ab": 8, "A": 9, "Bb": 10, "B": 11}
NOTE_NAMES = {"C": 0, "C#": 1, "Db": 1, "D": 2, "Eb": 3, "E": 4, "F": 5, "F#": 6, "G": 7, "Ab": 8, "A": 9, "Bb": 10, "B": 11}


def midi(name):
    return 12 * (int(name[-1]) + 1) + NOTE_NAMES[name[:-1]]


def hz(note):
    return 440.0 * 2 ** ((note - 69) / 12.0)


# Each bar is one chord, or two on beats 1 and 3.
A = [["F maj9"], ["F maj9"], ["A m7"], ["D 9"], ["G m9"], ["C 13"], ["F maj9"], ["C m7", "F 13"],
     ["Bb maj9"], ["Bb m9", "Eb 13"], ["A m9"], ["D 9"], ["G m9"], ["C 13"], ["F maj9"], ["G m7", "C 13"]]
B = [["D m9"], ["G 13"], ["C maj9"], ["C maj9"], ["C m9"], ["F 13"], ["Bb maj9"], ["Bb maj9"],
     ["Bb m9"], ["Eb 13"], ["A m9"], ["D 9"], ["G m9"], ["C 13"], ["F maj9"], ["G m7", "C 13"]]
INTRO = [["G m9"], ["C 13"], ["F maj9"], ["G m7", "C 13"]]
OUTRO = [["F maj9"], ["G m9", "C 13"], ["F maj9"], ["F maj9"]]

# (beat, length in beats, note) for each bar of the tune.
MELODY_A = [
    [(0, 1.5, "A5"), (1.5, 0.5, "G5"), (2, 2, "E5")],
    [(0, 1, "C5"), (1, 0.5, "E5"), (1.5, 2.5, "G5")],
    [(0, 1.5, "E5"), (1.5, 0.5, "D5"), (2, 2, "C5")],
    [(0.5, 1, "F#5"), (1.5, 0.5, "E5"), (2, 1, "D5"), (3, 1, "C5")],
    [(0, 1.5, "Bb4"), (1.5, 0.5, "D5"), (2, 2, "F5")],
    [(0, 1, "E5"), (1, 0.5, "D5"), (1.5, 1.5, "A5"), (3, 1, "G5")],
    [(0, 3, "E5")],
    [(1, 1, "Eb5"), (2, 1, "D5"), (3, 1, "C5")],
    [(0, 1.5, "D5"), (1.5, 0.5, "F5"), (2, 2, "A5")],
    [(0, 1, "Ab5"), (1, 1, "F5"), (2, 2, "G5")],
    [(0, 1.5, "G5"), (1.5, 0.5, "E5"), (2, 2, "C5")],
    [(0, 1, "F#5"), (1, 0.5, "A5"), (1.5, 1.5, "C6"), (3, 1, "A5")],
    [(0, 1.5, "Bb5"), (1.5, 0.5, "A5"), (2, 2, "F5")],
    [(0, 1, "G5"), (1, 1, "E5"), (2, 2, "A5")],
    [(0, 3, "G5")],
    [],
]
MELODY_B = [
    [(0, 1, "F5"), (1, 1, "A5"), (2, 2, "E5")],
    [(0, 1.5, "D5"), (1.5, 0.5, "B4"), (2, 2, "E5")],
    [(0, 1.5, "D5"), (1.5, 0.5, "E5"), (2, 2, "G5")],
    [(0, 3, "B4")],
    [(0, 1, "Eb5"), (1, 1, "G5"), (2, 2, "D5")],
    [(0, 1, "A4"), (1, 1, "C5"), (2, 2, "D5")],
    [(0, 1, "C5"), (1, 1, "D5"), (2, 2, "F5")],
    [(0, 3, "A5")],
    [(0, 1.5, "F5"), (1.5, 0.5, "Db5"), (2, 2, "C5")],
    [(0, 1, "Bb4"), (1, 1, "C5"), (2, 2, "G5")],
    [(0, 1.5, "G5"), (1.5, 0.5, "E5"), (2, 2, "B4")],
    [(0, 1.5, "F#5"), (1.5, 0.5, "E5"), (2, 2, "A5")],
    [(0, 1, "F5"), (1, 1, "D5"), (2, 2, "A5")],
    [(0, 1, "A5"), (1, 1, "G5"), (2, 2, "E5")],
    [(0, 3, "F5")],
    [],
]

SECTIONS = [("intro", INTRO, None), ("a", A, MELODY_A), ("b", B, MELODY_B), ("solo", A, "solo"), ("b2", B, MELODY_B), ("a2", A, MELODY_A), ("outro", OUTRO, None)]
TOTAL_BARS = sum(len(chords) for _, chords, _ in SECTIONS)
LENGTH = int((TOTAL_BARS * BAR + 4.0) * SR)


def bus():
    return np.zeros((LENGTH, 2))


def add(target, start_seconds, mono, gain, pan=0.0):
    start = int(max(0, start_seconds) * SR)
    end = min(LENGTH, start + len(mono))
    if end <= start:
        return
    part = mono[: end - start] * gain
    left, right = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
    target[start:end, 0] += part * left
    target[start:end, 1] += part * right


def envelope(t, length, attack, decay, release):
    env = np.minimum(1.0, t / attack) * np.exp(-t * decay)
    tail = np.clip((length + release - t) / release, 0.0, 1.0)
    return env * tail


def timeline(length, release):
    return np.arange(int((length + release) * SR)) / SR


def electric_piano(freq, length, velocity):
    t = timeline(length, 0.22)
    index = (0.9 + 2.3 * velocity) * np.exp(-t * 4.5) + 0.32
    body = np.sin(2 * np.pi * freq * t + index * np.sin(2 * np.pi * freq * t))
    tine = 0.2 * velocity * np.sin(2 * np.pi * min(freq * 14, 11000) * t) * np.exp(-t * 22)
    bark = velocity * (0.22 * np.sin(2 * np.pi * min(freq * 5, 11000) * t) * np.exp(-t * 9) + 0.12 * np.sin(2 * np.pi * min(freq * 8, 12000) * t) * np.exp(-t * 14))
    return (body + tine + bark) * envelope(t, length, 0.004, 1.15, 0.22)


def vibraphone(freq, length, velocity):
    t = timeline(length, 0.5)
    tone = np.sin(2 * np.pi * freq * t) + 0.42 * np.sin(2 * np.pi * freq * 4 * t) * np.exp(-t * 6) + 0.14 * np.sin(2 * np.pi * freq * 10 * t) * np.exp(-t * 16)
    tremolo = 1 - 0.14 * (0.5 + 0.5 * np.sin(2 * np.pi * 4.6 * t))
    return tone * tremolo * envelope(t, length, 0.003, 0.75, 0.5) * velocity


def bass(freq, length, velocity):
    t = timeline(length, 0.06)
    tone = np.sin(2 * np.pi * freq * t) + 0.32 * np.sin(4 * np.pi * freq * t) + 0.1 * np.sin(6 * np.pi * freq * t) * np.exp(-t * 7)
    return tone * envelope(t, length, 0.008, 1.5, 0.06) * velocity


def pad(freq, length):
    t = timeline(length, 0.6)
    tone = np.sin(2 * np.pi * freq * t) + np.sin(2 * np.pi * freq * 1.0016 * t) + 0.25 * np.sin(4 * np.pi * freq * 0.9988 * t)
    return tone * np.minimum(1.0, t / 0.55) * np.clip((length + 0.6 - t) / 0.6, 0, 1)


def kick():
    t = timeline(0.28, 0.02)
    freq = 46 + 52 * np.exp(-t * 38)
    return np.sin(2 * np.pi * np.cumsum(freq) / SR) * np.exp(-t * 11)


def rim():
    t = timeline(0.06, 0.01)
    noise = rng.normal(0, 0.3, len(t))
    return (np.sin(2 * np.pi * 1720 * t) + 0.6 * np.sin(2 * np.pi * 860 * t) + noise) * np.exp(-t * 95)


def shaker(accent):
    t = timeline(0.09, 0.01)
    # Mostly the upper mids: the very top of raw noise only sounds like hiss.
    noise = np.diff(rng.normal(0, 1, len(t) + 9))
    noise = np.convolve(noise, [0.1, 0.2, 0.4, 0.2, 0.1, 0.0, -0.3, -0.3, -0.3], mode="valid")[: len(t)]
    return noise * np.minimum(1.0, t / 0.006) * np.exp(-t * 42) * accent


def voice_chord(chord):
    root, quality = chord.split()
    pc = ROOTS[root]
    notes = []
    for interval in QUALITIES[quality]:
        note = 48 + (pc + interval) % 12
        while note < 57:
            note += 12
        while note > 71:
            note -= 12
        notes.append(note)
    return sorted(set(notes)), pc


def bass_note(pc, up=0):
    note = 33 + (pc + up - 9) % 12
    return note


def clashes(chord, note):
    root, quality = chord.split()
    allowed = {(ROOTS[root] + i) % 12 for i in QUALITIES[quality]} | {ROOTS[root]}
    allowed |= {(ROOTS[root] + i) % 12 for i in {"maj9": [9], "m9": [5, 17], "m7": [5, 14], "9": [9, 2], "13": [2, 9, 7]}[quality]}
    return note % 12 not in allowed


keys, tune, low, warm, drums = bus(), bus(), bus(), bus(), bus()
BAR_PATTERN = [[0, 1.5, 3], [1, 2.5]]  # the two-bar comping figure
problems = 0
solo_pool = {"maj9": [0, 4, 7, 11, 14], "m9": [0, 3, 7, 10, 14], "m7": [0, 3, 7, 10], "9": [0, 4, 7, 10, 14], "13": [4, 7, 10, 14, 21]}
last_solo = 72
bar_index = 0
section_starts = {}

for name, chords, line in SECTIONS:
    section_starts[name] = bar_index
    for inside, bar in enumerate(chords):
        at = bar_index * BAR
        half = len(bar) == 2
        chord_at = lambda beat: bar[1] if half and beat >= 2 else bar[0]
        section_gain = 0.82 if name in ("intro", "outro") else 1.0
        drums_on = name not in ("intro", "outro") or (name == "intro" and inside >= 2)

        # Comping
        for beat in BAR_PATTERN[bar_index % 2]:
            notes, _ = voice_chord(chord_at(beat))
            length = 0.9 * BEAT
            for i, note in enumerate(notes):
                when = at + beat * BEAT + rng.normal(0, 0.004) + i * 0.003
                add(keys, when, electric_piano(hz(note), length, 0.42 + rng.uniform(-0.05, 0.08)), 0.1 * section_gain, -0.28 + i * 0.18)

        # A warm bed under the harmony
        for part, chord in enumerate(bar):
            notes, _ = voice_chord(chord)
            span = (2 if half else 4) * BEAT
            for i, note in enumerate(notes[:3]):
                add(warm, at + part * 2 * BEAT, pad(hz(note - 12), span), 0.03, -0.5 + i * 0.5)

        # Bass: the root and fifth in the classic dotted bossa pattern
        for beat, up, length, velocity in [(0, 0, 1.4, 1.0), (1.5, 0, 0.45, 0.72), (2, 7, 1.4, 0.92), (3.5, 7, 0.45, 0.68)]:
            _, pc = voice_chord(chord_at(beat))
            note = bass_note(pc, up)
            add(low, at + beat * BEAT + rng.normal(0, 0.003), bass(hz(note), length * BEAT, velocity), 0.27 * section_gain)

        if drums_on:
            for beat in (0, 1.5, 2, 3.5):
                add(drums, at + beat * BEAT, kick(), 0.2 if beat in (0, 2) else 0.11)
            for beat in BAR_PATTERN[bar_index % 2]:
                add(drums, at + beat * BEAT + rng.normal(0, 0.003), rim(), 0.11, 0.2)
            for step in range(8):
                add(drums, at + step * BEAT / 2 + rng.normal(0, 0.003), shaker(0.3 if step % 2 else 0.17), 1.0, -0.2)

        # The tune
        if isinstance(line, list):
            for beat, length, note_name in line[inside]:
                note = midi(note_name)
                if beat % 2 == 0 and clashes(chord_at(beat), note):
                    problems += 1
                    print("check", name, inside + 1, beat, note_name, chord_at(beat))
                tune_gain = 0.16 if name != "a2" else 0.145
                add(tune, at + beat * BEAT + rng.normal(0, 0.006), vibraphone(hz(note), length * BEAT, 0.9 + rng.uniform(-0.08, 0.06)), tune_gain, 0.12)
                if name in ("a2", "b2"):
                    add(tune, at + beat * BEAT + 0.012, electric_piano(hz(note - 12), length * BEAT, 0.34), 0.11, -0.2)
        elif line == "solo":
            # A short, chord-tone keyboard solo: two-bar phrases that breathe between them.
            if inside % 2 == 0:
                for beat in [0, 0.5, 1.5, 2, 3, 3.5] if inside % 4 == 0 else [0.5, 1, 2, 2.5, 3.5]:
                    chord = chord_at(beat)
                    root, quality = chord.split()
                    pool = [48 + 12 * o + (ROOTS[root] + i) % 12 for o in range(2, 4) for i in solo_pool[quality]]
                    pool = [p for p in pool if 69 <= p <= 86]
                    near = sorted(pool, key=lambda p: abs(p - last_solo) + rng.uniform(0, 2.2))
                    last_solo = int(near[min(1, len(near) - 1)])
                    add(tune, at + beat * BEAT + rng.normal(0, 0.006), electric_piano(hz(last_solo), 0.5 * BEAT, 0.62 + rng.uniform(-0.1, 0.1)), 0.23, 0.08)

        bar_index += 1


# Reverb from decaying filtered noise, a little different on each side.
def impulse():
    n = int(1.9 * SR)
    t = np.arange(n) / SR
    noise = rng.normal(0, 1, n) * np.exp(-t / 0.42)
    kernel = np.ones(14) / 14
    noise = np.convolve(noise, kernel, mode="same")
    noise[: int(0.018 * SR)] = 0
    return noise / np.sqrt(np.sum(noise ** 2))


def convolve(signal, response):
    size = len(signal) + len(response)
    return np.fft.irfft(np.fft.rfft(signal, size) * np.fft.rfft(response, size), size)[: len(signal)]


dry = keys + tune + low + warm + drums
send = keys * 0.28 + tune * 0.5 + warm * 0.35 + drums * 0.12
wet = np.stack([convolve(send[:, side], impulse()) for side in (0, 1)], axis=1)
mix = dry + wet * 0.9

mix = np.tanh(mix * 1.25) / 1.25
target_rms = 10 ** (-17.5 / 20)
loud_part = mix[int(BAR * 6 * SR): int(BAR * 40 * SR)]
mix *= target_rms / np.sqrt(np.mean(loud_part ** 2))
peak = float(np.max(np.abs(mix)))
if peak > 0.93:
    mix *= 0.93 / peak

# Gentle ends, so the loop back to the start is smooth.
fade_in = int(1.2 * SR)
mix[:fade_in] *= np.linspace(0, 1, fade_in)[:, None] ** 1.5
fade_out = int(5.0 * SR)
mix[-fade_out:] *= np.linspace(1, 0, fade_out)[:, None] ** 1.6

print("bars", TOTAL_BARS, "seconds", round(LENGTH / SR, 1), "melody clashes", problems)
print("peak dB", round(20 * np.log10(np.max(np.abs(mix))), 2))
for name, start in section_starts.items():
    begin, end = int(start * BAR * SR), int((start + 4) * BAR * SR)
    print(name.ljust(6), "rms dB", round(20 * np.log10(np.sqrt(np.mean(mix[begin:end] ** 2))), 1))
spectrum = np.abs(np.fft.rfft(mix[int(10 * SR): int(40 * SR), 0])) ** 2
frequency = np.fft.rfftfreq(int(30 * SR), 1 / SR)
total = spectrum.sum()
for low_edge, high_edge in [(20, 120), (120, 500), (500, 2000), (2000, 8000), (8000, 20000)]:
    share = spectrum[(frequency >= low_edge) & (frequency < high_edge)].sum() / total
    print(f"energy {low_edge}-{high_edge} Hz: {100 * share:.1f}%")

pcm = (np.clip(mix, -1, 1) * 32767).astype(np.int16)
out = pathlib.Path(sys.argv[1])
out.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(out.with_suffix(".wav")), "wb") as wav:
    wav.setnchannels(2)
    wav.setsampwidth(2)
    wav.setframerate(SR)
    wav.writeframes(pcm.tobytes())

encoder = lameenc.Encoder()
encoder.set_bit_rate(128)
encoder.set_in_sample_rate(SR)
encoder.set_channels(2)
encoder.set_quality(2)
data = encoder.encode(pcm.tobytes()) + encoder.flush()
out.write_bytes(bytes(data))
print("mp3 bytes", len(data))
