"""
Generate sound packs for PixelHelper:
- Sound (melodic sci-fi synth sound effects)
- VoiceAdult (robotic voice synthesized with metallic vocoder effect)
- VoiceChild (cute high-pitched electronic robotic voice)

All generated as standard 16-bit 44.1kHz mono WAV files.
"""

import os
import sys
import math
import struct
import wave
import subprocess
import tempfile

SAMPLE_RATE = 44100

def write_wav(filename, samples, sample_rate=SAMPLE_RATE):
    os.makedirs(os.path.dirname(filename), exist_ok=True)
    # Normalize
    max_val = max((abs(s) for s in samples), default=0.0)
    if max_val > 0.001:
        gain = 0.88 / max_val
        samples = [s * gain for s in samples]
    else:
        samples = list(samples)

    int_samples = []
    for s in samples:
        v = int(max(-1.0, min(1.0, s)) * 32767.0)
        int_samples.append(v)

    with wave.open(filename, 'wb') as wav_file:
        wav_file.setnchannels(1)
        wav_file.setsampwidth(2)
        wav_file.setframerate(sample_rate)
        wav_file.writeframes(struct.pack(f'<{len(int_samples)}h', *int_samples))
    print(f"Generated: {filename} ({len(int_samples)} samples, {len(int_samples)/sample_rate:.2f}s)")

def read_wav(filename):
    with wave.open(filename, 'rb') as wav_file:
        nchannels = wav_file.getnchannels()
        sampwidth = wav_file.getsampwidth()
        framerate = wav_file.getframerate()
        nframes = wav_file.getnframes()
        raw = wav_file.readframes(nframes)
    
    samples = []
    if sampwidth == 2:
        total = nframes * nchannels
        vals = struct.unpack(f'<{total}h', raw)
        if nchannels == 1:
            samples = [v / 32768.0 for v in vals]
        else:
            # mix down to mono
            samples = [(vals[i*2] + vals[i*2+1]) / (2.0 * 32768.0) for i in range(nframes)]
    return samples, framerate

# --- 1. Sound FX Synthesis ---

def gen_send_fx():
    # Ascending sci-fi sweep + soft chime: 0.28s
    duration = 0.28
    total_samples = int(duration * SAMPLE_RATE)
    samples = []
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        env = math.sin(math.pi * (t / duration)) ** 1.2
        # frequency sweep 700 to 2200 Hz
        freq = 700.0 + 1500.0 * (t / duration) ** 1.5
        phase = 2.0 * math.pi * (700.0 * t + 1500.0 * (t ** 2.5) / 2.5)
        # add pleasant 2nd harmonic
        sig = 0.75 * math.sin(phase) + 0.25 * math.sin(phase * 2.0)
        samples.append(sig * env)
    return samples

def gen_receive_fx():
    # Celestial dual-bell chime (E6: 1318Hz, G#6: 1661Hz, B6: 1975Hz): 0.5s
    duration = 0.5
    total_samples = int(duration * SAMPLE_RATE)
    samples = []
    notes = [
        (0.00, 1318.5, 0.40), # E6
        (0.07, 1661.2, 0.45), # G#6
        (0.14, 1975.5, 0.50), # B6
    ]
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        sig = 0.0
        for start_t, freq, amp in notes:
            if t >= start_t:
                dt = t - start_t
                env = math.exp(-7.5 * dt) * amp
                # fundamental + soft bell harmonic (3x frequency)
                wave_val = 0.8 * math.sin(2.0 * math.pi * freq * dt) + 0.2 * math.sin(2.0 * math.pi * freq * 2.76 * dt)
                sig += wave_val * env
        samples.append(sig)
    return samples

def gen_dance_fx():
    # Upbeat 8-bit retro chiptune loop (approx 2.6s)
    # BPM 135 -> ~0.11s per 16th note
    melody = [
        # note frequencies in Hz
        523.25, 659.25, 783.99, 1046.50, # C5, E5, G5, C6
        880.00, 1046.50, 880.00, 783.99, # A5, C6, A5, G5
        659.25, 783.99, 659.25, 587.33, # E5, G5, E5, D5
        523.25, 587.33, 659.25, 783.99, # C5, D5, E5, G5
        1046.50, 1318.51, 1567.98, 1318.51, # C6, E6, G6, E6
        1046.50, 880.00, 783.99, 1046.50 # C6, A5, G5, C6
    ]
    note_dur = 0.11
    samples = []
    for note_idx, freq in enumerate(melody):
        note_samples = int(note_dur * SAMPLE_RATE)
        for i in range(note_samples):
            t = i / SAMPLE_RATE
            # square/pulse wave with 25% duty cycle
            phase = (freq * t) % 1.0
            pulse = 1.0 if phase < 0.35 else -1.0
            env = math.exp(-9.0 * (t / note_dur))
            sig = pulse * env * 0.7
            # bass line accompanying
            bass_freq = freq / 4.0
            bass_phase = (bass_freq * t) % 1.0
            bass_sig = (1.0 if bass_phase < 0.5 else -1.0) * math.exp(-6.0 * (t / note_dur)) * 0.3
            samples.append(sig + bass_sig)
    return samples

def gen_urgent_fx():
    # Urgent sci-fi attention pulse: 0.75s (two pulses at 980Hz and 1320Hz)
    duration = 0.75
    total_samples = int(duration * SAMPLE_RATE)
    samples = []
    pulses = [
        (0.00, 987.77, 0.28),  # B5
        (0.24, 1318.51, 0.32), # E6
    ]
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        sig = 0.0
        for start_t, freq, dur in pulses:
            if start_t <= t < start_t + dur:
                dt = t - start_t
                env = math.sin(math.pi * (dt / dur)) ** 0.8
                # FM warble modulation
                mod = 40.0 * math.sin(2.0 * math.pi * 30.0 * dt)
                wave_val = math.sin(2.0 * math.pi * (freq + mod) * dt)
                sig += wave_val * env * 0.85
        samples.append(sig)
    return samples

# --- 2. TTS Voice Processing ---

def tts_to_file(text, out_path, rate=0):
    ps_cmd = f"""
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {{
    $synth.SelectVoice('Microsoft Irina Desktop')
}} catch {{}}
$synth.Rate = {rate}
$synth.SetOutputToWaveFile('{out_path}')
$synth.Speak('{text}')
$synth.Dispose()
"""
    subprocess.run(["powershell", "-Command", ps_cmd], check=True, capture_output=True)

def robotize_adult(samples, sample_rate):
    # Robotize: Vocoder ring-mod carrier (75Hz) + metallic feedback comb filter (14ms)
    carrier_freq = 75.0
    delay_samples = int(0.014 * sample_rate)
    delay_buf = [0.0] * (len(samples) + delay_samples + 100)
    out = []
    for i, s in enumerate(samples):
        t = i / sample_rate
        # ring modulator
        carrier = math.sin(2.0 * math.pi * carrier_freq * t)
        ring = s * carrier
        # dry / wet mix
        robot = 0.65 * s + 0.35 * ring
        # comb filter echo
        comb = robot + 0.35 * delay_buf[i]
        delay_buf[i + delay_samples] = comb
        out.append(comb)
    return out

def pitch_shift_child(samples, sample_rate, pitch_ratio=1.35):
    # Resample to pitch up
    new_len = int(len(samples) / pitch_ratio)
    resampled = []
    for i in range(new_len):
        src_idx = i * pitch_ratio
        idx_floor = int(src_idx)
        idx_ceil = min(idx_floor + 1, len(samples) - 1)
        frac = src_idx - idx_floor
        val = samples[idx_floor] * (1.0 - frac) + samples[idx_ceil] * frac
        resampled.append(val)
    
    # Add subtle high-freq robotic shimmer (carrier 140Hz)
    out = []
    for i, s in enumerate(resampled):
        t = i / sample_rate
        shimmer = 0.15 * s * math.sin(2.0 * math.pi * 140.0 * t)
        out.append(s + shimmer)
    return out

def main():
    base_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "Assets", "Sounds"))
    print(f"Generating sounds in: {base_dir}")

    # --- 1. Sound FX Profile ---
    sound_dir = os.path.join(base_dir, "Sound")
    write_wav(os.path.join(sound_dir, "send.wav"), gen_send_fx())
    write_wav(os.path.join(sound_dir, "receive.wav"), gen_receive_fx())
    write_wav(os.path.join(sound_dir, "dance.wav"), gen_dance_fx())
    write_wav(os.path.join(sound_dir, "urgent.wav"), gen_urgent_fx())

    # --- 2. Voice Adult Profile ---
    voice_adult_dir = os.path.join(base_dir, "VoiceAdult")
    voice_child_dir = os.path.join(base_dir, "VoiceChild")

    phrases = {
        "send": ("Отправлено", "Отправлено!"),
        "receive": ("Новое сообщение", "Вам сообщение!"),
        "dance": ("Танцуем!", "Ура, танцуем!"),
        "urgent": ("Внимание, срочное сообщение!", "Срочно, посмотри!")
    }

    with tempfile.TemporaryDirectory() as tmp_dir:
        for event, (adult_text, child_text) in phrases.items():
            # Adult
            adult_raw = os.path.join(tmp_dir, f"adult_{event}.wav")
            tts_to_file(adult_text, adult_raw, rate=0)
            adult_samples, sr = read_wav(adult_raw)
            adult_robot = robotize_adult(adult_samples, sr)
            write_wav(os.path.join(voice_adult_dir, f"{event}.wav"), adult_robot, sr)

            # Child
            child_raw = os.path.join(tmp_dir, f"child_{event}.wav")
            tts_to_file(child_text, child_raw, rate=1)
            child_samples, sr = read_wav(child_raw)
            child_robot = pitch_shift_child(child_samples, sr, pitch_ratio=1.35)
            write_wav(os.path.join(voice_child_dir, f"{event}.wav"), child_robot, sr)

    print("All sound packs successfully generated!")

if __name__ == "__main__":
    main()
