"""
Generate sound packs for PixelHelper using Neural Edge TTS:
- Sound (melodic sci-fi synth sound effects)
- VoiceAdult/{lang} (robotic voice synthesized with metallic vocoder effect in RU, KK, EN, ZH)
- VoiceChild/{lang} (cute high-pitched electronic robotic voice in RU, KK, EN, ZH, with randomized receive phrases)

All generated as standard 16-bit 44.1kHz mono WAV files.
"""

import os
import sys
import math
import struct
import wave
import asyncio
import tempfile
import shutil

try:
    import edge_tts
except ImportError:
    edge_tts = None

try:
    import miniaudio
except ImportError:
    miniaudio = None

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

# --- 1. Sound FX Synthesis ---

def gen_send_fx():
    duration = 0.28
    total_samples = int(duration * SAMPLE_RATE)
    samples = []
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        env = math.sin(math.pi * (t / duration)) ** 1.2
        phase = 2.0 * math.pi * (700.0 * t + 1500.0 * (t ** 2.5) / 2.5)
        sig = 0.75 * math.sin(phase) + 0.25 * math.sin(phase * 2.0)
        samples.append(sig * env)
    return samples

def gen_receive_fx():
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
                wave_val = 0.8 * math.sin(2.0 * math.pi * freq * dt) + 0.2 * math.sin(2.0 * math.pi * freq * 2.76 * dt)
                sig += wave_val * env
        samples.append(sig)
    return samples

def gen_dance_fx():
    melody = [
        523.25, 659.25, 783.99, 1046.50,
        880.00, 1046.50, 880.00, 783.99,
        659.25, 783.99, 659.25, 587.33,
        523.25, 587.33, 659.25, 783.99,
        1046.50, 1318.51, 1567.98, 1318.51,
        1046.50, 880.00, 783.99, 1046.50
    ]
    note_dur = 0.11
    samples = []
    for note_idx, freq in enumerate(melody):
        note_samples = int(note_dur * SAMPLE_RATE)
        for i in range(note_samples):
            t = i / SAMPLE_RATE
            phase = (freq * t) % 1.0
            pulse = 1.0 if phase < 0.35 else -1.0
            env = math.exp(-9.0 * (t / note_dur))
            sig = pulse * env * 0.7
            bass_freq = freq / 4.0
            bass_phase = (bass_freq * t) % 1.0
            bass_sig = (1.0 if bass_phase < 0.5 else -1.0) * math.exp(-6.0 * (t / note_dur)) * 0.3
            samples.append(sig + bass_sig)
    return samples

def gen_urgent_fx():
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
                mod = 40.0 * math.sin(2.0 * math.pi * 30.0 * dt)
                wave_val = math.sin(2.0 * math.pi * (freq + mod) * dt)
                sig += wave_val * env * 0.85
        samples.append(sig)
    return samples

# --- 2. Neural TTS Voice Processing ---

VOICE_MAP = {
    "ru": "ru-RU-SvetlanaNeural",
    "kk": "kk-KZ-AigulNeural",
    "en": "en-US-JennyNeural",
    "zh": "zh-CN-XiaoxiaoNeural"
}

async def fetch_tts_edge(text, lang="ru"):
    voice = VOICE_MAP.get(lang, "ru-RU-SvetlanaNeural")
    communicate = edge_tts.Communicate(text, voice)
    mp3_data = bytearray()
    async for chunk in communicate.stream():
        if chunk["type"] == "audio":
            mp3_data.extend(chunk["data"])
    
    if miniaudio and len(mp3_data) > 200:
        decoded = miniaudio.decode(bytes(mp3_data))
        raw = decoded.samples
        if decoded.nchannels == 2:
            mono = [(raw[i*2] + raw[i*2+1]) / (2.0 * 32768.0) for i in range(len(raw)//2)]
        else:
            mono = [v / 32768.0 for v in raw]
        if decoded.sample_rate != SAMPLE_RATE:
            ratio = decoded.sample_rate / SAMPLE_RATE
            new_len = int(len(mono) / ratio)
            resampled = [mono[int(i * ratio)] for i in range(new_len)]
            return resampled, SAMPLE_RATE
        return mono, SAMPLE_RATE
    return [], SAMPLE_RATE

def tts_fetch_audio(text, lang="ru"):
    try:
        loop = asyncio.get_event_loop()
    except RuntimeError:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
    
    samples, sr = loop.run_until_complete(fetch_tts_edge(text, lang=lang))
    if samples and len(samples) > 100:
        return samples, sr
    
    print(f"Edge TTS returned empty for {lang} '{text}', generating synthetic tone fallback")
    dur = 0.8
    s = [0.3 * math.sin(2 * math.pi * 440 * i / SAMPLE_RATE) for i in range(int(dur * SAMPLE_RATE))]
    return s, SAMPLE_RATE

def robotize_adult(samples, sample_rate):
    carrier_freq = 75.0
    delay_samples = int(0.014 * sample_rate)
    delay_buf = [0.0] * (len(samples) + delay_samples + 100)
    out = []
    for i, s in enumerate(samples):
        t = i / sample_rate
        carrier = math.sin(2.0 * math.pi * carrier_freq * t)
        ring = s * carrier
        robot = 0.65 * s + 0.35 * ring
        comb = robot + 0.35 * delay_buf[i]
        delay_buf[i + delay_samples] = comb
        out.append(comb)
    return out

def pitch_shift_child(samples, sample_rate, pitch_ratio=1.35):
    new_len = int(len(samples) / pitch_ratio)
    resampled = []
    for i in range(new_len):
        src_idx = i * pitch_ratio
        idx_floor = int(src_idx)
        idx_ceil = min(idx_floor + 1, len(samples) - 1)
        frac = src_idx - idx_floor
        val = samples[idx_floor] * (1.0 - frac) + samples[idx_ceil] * frac
        resampled.append(val)
    
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

    # --- 2. Multilingual Voice Profiles ---
    multilingual_phrases = {
        "ru": {
            "adult": {
                "send": "Доставил",
                "dance": "Женщина я не танцую",
                "receive": "Новое сообщение",
                "urgent": "Внимание, срочное сообщение!"
            },
            "child": {
                "send": "Доставил!",
                "dance": "Ура, танцуем!",
                "receive_1": "Вам письмо",
                "receive_2": "Новое сообщение",
                "urgent": "Посмотрите! Посмотрите! Посмотрите! Это срочно!"
            }
        },
        "kk": {
            "adult": {
                "send": "Жеткіздім",
                "dance": "Ханым, мен билемеймін",
                "receive": "Жаңа хабарлама",
                "urgent": "Назар аударыңыз, шұғыл хабарлама!"
            },
            "child": {
                "send": "Жеткіздім!",
                "dance": "Алақай, билейміз!",
                "receive_1": "Сізге хат келді",
                "receive_2": "Жаңа хабарлама",
                "urgent": "Қараңызшы! Қараңызшы! Қараңызшы! Бұл шұғыл!"
            }
        },
        "en": {
            "adult": {
                "send": "Delivered",
                "dance": "I don't dance",
                "receive": "New message",
                "urgent": "Attention, urgent message!"
            },
            "child": {
                "send": "Delivered!",
                "dance": "Yay, let's dance!",
                "receive_1": "You've got mail",
                "receive_2": "New message",
                "urgent": "Look! Look! Look! It's urgent!"
            }
        },
        "zh": {
            "adult": {
                "send": "已送达",
                "dance": "女士我不会跳舞",
                "receive": "新消息",
                "urgent": "注意，紧急消息！"
            },
            "child": {
                "send": "已送达！",
                "dance": "耶，跳舞啦！",
                "receive_1": "您的信件",
                "receive_2": "新消息",
                "urgent": "快看！快看！快看！这很紧急！"
            }
        }
    }

    voice_adult_base = os.path.join(base_dir, "VoiceAdult")
    voice_child_base = os.path.join(base_dir, "VoiceChild")

    for lang, profiles in multilingual_phrases.items():
        print(f"\n--- Processing Language: {lang} ---")
        lang_adult_dir = os.path.join(voice_adult_base, lang)
        lang_child_dir = os.path.join(voice_child_base, lang)

        # Adult phrases
        for event, text in profiles["adult"].items():
            samples, sr = tts_fetch_audio(text, lang=lang)
            adult_robot = robotize_adult(samples, sr)
            out_file = os.path.join(lang_adult_dir, f"{event}.wav")
            write_wav(out_file, adult_robot, sr)

            if lang == "ru":
                write_wav(os.path.join(voice_adult_base, f"{event}.wav"), adult_robot, sr)

        # Child phrases
        for event, text in profiles["child"].items():
            samples, sr = tts_fetch_audio(text, lang=lang)
            child_robot = pitch_shift_child(samples, sr, pitch_ratio=1.35)
            out_file = os.path.join(lang_child_dir, f"{event}.wav")
            write_wav(out_file, child_robot, sr)

            if event == "receive_1":
                write_wav(os.path.join(lang_child_dir, "receive.wav"), child_robot, sr)

            if lang == "ru":
                if event == "receive_1":
                    write_wav(os.path.join(voice_child_base, "receive.wav"), child_robot, sr)
                    write_wav(os.path.join(voice_child_base, "receive_1.wav"), child_robot, sr)
                elif event == "receive_2":
                    write_wav(os.path.join(voice_child_base, "receive_2.wav"), child_robot, sr)
                else:
                    write_wav(os.path.join(voice_child_base, f"{event}.wav"), child_robot, sr)

    print("\nAll multilingual neural sound packs successfully generated!")

if __name__ == "__main__":
    main()
