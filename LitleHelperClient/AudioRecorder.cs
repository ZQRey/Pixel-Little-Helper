using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PixelHelper;

public sealed class AudioRecorder : IDisposable
{
    #region WinMM P/Invoke Definitions

    private const int MMSYSERR_NOERROR = 0;
    private const int CALLBACK_FUNCTION = 0x00030000;
    private const int WIM_DATA = 0x03C0;
    private const int WIM_CLOSE = 0x03BF;
    private const int WAVE_MAPPER = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    private delegate void WaveInProc(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    [DllImport("winmm.dll", EntryPoint = "waveInGetNumDevs")]
    private static extern int waveInGetNumDevs();

    [DllImport("winmm.dll", EntryPoint = "waveInOpen")]
    private static extern int waveInOpen(out IntPtr phwi, int uDeviceID, ref WAVEFORMATEX pwfx, WaveInProc dwCallback, IntPtr dwInstance, int fdwOpen);

    [DllImport("winmm.dll", EntryPoint = "waveInPrepareHeader")]
    private static extern int waveInPrepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);

    [DllImport("winmm.dll", EntryPoint = "waveInUnprepareHeader")]
    private static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);

    [DllImport("winmm.dll", EntryPoint = "waveInAddBuffer")]
    private static extern int waveInAddBuffer(IntPtr hwi, IntPtr pwh, int cbwh);

    [DllImport("winmm.dll", EntryPoint = "waveInStart")]
    private static extern int waveInStart(IntPtr hwi);

    [DllImport("winmm.dll", EntryPoint = "waveInReset")]
    private static extern int waveInReset(IntPtr hwi);

    [DllImport("winmm.dll", EntryPoint = "waveInClose")]
    private static extern int waveInClose(IntPtr hwi);

    #endregion

    private readonly object syncRoot = new();
    private readonly WaveInProc waveInCallback;
    private IntPtr hWaveIn = IntPtr.Zero;
    private MemoryStream? recordedStream;
    private readonly List<IntPtr> allocatedHeaders = new();
    private readonly List<IntPtr> allocatedBuffers = new();
    private string? currentFilePath;
    private DateTime startTime;
    private bool isRecording;
    private uint activeSampleRate = 44100;

    public bool IsRecording => isRecording;
    public TimeSpan Duration => isRecording ? DateTime.UtcNow - startTime : TimeSpan.Zero;

    public AudioRecorder()
    {
        waveInCallback = DataCallback;
    }

    public void Start()
    {
        lock (syncRoot)
        {
            if (isRecording) return;

            int devCount = waveInGetNumDevs();
            if (devCount <= 0)
            {
                throw new InvalidOperationException("В системе не обнаружено ни одного микрофона. Проверьте подключение аудиоустройства.");
            }

            // Пробуем форматы: 44.1 kHz -> 48 kHz -> 22.05 kHz -> 16 kHz (16-bit mono PCM)
            uint[] candidateRates = [44100, 48000, 22050, 16000];
            int openResult = -1;

            foreach (var rate in candidateRates)
            {
                var wfx = new WAVEFORMATEX
                {
                    wFormatTag = 1, // WAVE_FORMAT_PCM
                    nChannels = 1,  // Mono
                    nSamplesPerSec = rate,
                    wBitsPerSample = 16,
                    nBlockAlign = 2,
                    nAvgBytesPerSec = rate * 2,
                    cbSize = 0
                };

                openResult = waveInOpen(out hWaveIn, WAVE_MAPPER, ref wfx, waveInCallback, IntPtr.Zero, CALLBACK_FUNCTION);
                if (openResult == MMSYSERR_NOERROR)
                {
                    activeSampleRate = rate;
                    break;
                }
            }

            if (openResult != MMSYSERR_NOERROR || hWaveIn == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Не удалось открыть микрофон для записи (ошибка winmm {openResult}). Убедитесь, что микрофон не занят другим приложением и доступ разрешен в настройках Windows.");
            }

            recordedStream = new MemoryStream();
            allocatedHeaders.Clear();
            allocatedBuffers.Clear();

            // Создаем 4 буфера по 100 мс для непрерывной записи без задержек
            int bufferSize = (int)(activeSampleRate * 2 / 10); // 100 ms of 16-bit mono PCM
            int headerSize = Marshal.SizeOf<WAVEHDR>();

            for (int i = 0; i < 4; i++)
            {
                IntPtr pBuffer = Marshal.AllocHGlobal(bufferSize);
                allocatedBuffers.Add(pBuffer);

                var hdr = new WAVEHDR
                {
                    lpData = pBuffer,
                    dwBufferLength = (uint)bufferSize,
                    dwBytesRecorded = 0,
                    dwFlags = 0
                };

                IntPtr pHdr = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(hdr, pHdr, false);
                allocatedHeaders.Add(pHdr);

                waveInPrepareHeader(hWaveIn, pHdr, headerSize);
                waveInAddBuffer(hWaveIn, pHdr, headerSize);
            }

            int startRes = waveInStart(hWaveIn);
            if (startRes != MMSYSERR_NOERROR)
            {
                CleanupUnmanaged();
                throw new InvalidOperationException($"Не удалось начать захват звука с микрофона (код {startRes}).");
            }

            startTime = DateTime.UtcNow;
            isRecording = true;
        }
    }

    private void DataCallback(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
    {
        if (uMsg != WIM_DATA || !isRecording || dwParam1 == IntPtr.Zero) return;

        try
        {
            var hdr = Marshal.PtrToStructure<WAVEHDR>(dwParam1);
            if (hdr.dwBytesRecorded > 0 && hdr.lpData != IntPtr.Zero)
            {
                byte[] chunk = new byte[hdr.dwBytesRecorded];
                Marshal.Copy(hdr.lpData, chunk, 0, (int)hdr.dwBytesRecorded);

                lock (syncRoot)
                {
                    if (isRecording && recordedStream != null)
                    {
                        recordedStream.Write(chunk, 0, chunk.Length);
                    }
                }
            }

            // Передаем буфер обратно драйверу для продолжения записи
            if (isRecording && hWaveIn != IntPtr.Zero)
            {
                waveInAddBuffer(hWaveIn, dwParam1, Marshal.SizeOf<WAVEHDR>());
            }
        }
        catch { }
    }

    public (string filePath, int durationSeconds, string waveformJson) Stop()
    {
        byte[] pcmData;
        int durationSeconds;
        uint rate;

        lock (syncRoot)
        {
            if (!isRecording) throw new InvalidOperationException("Запись не активна.");
            isRecording = false;

            var elapsed = DateTime.UtcNow - startTime;
            durationSeconds = Math.Max(1, (int)Math.Round(elapsed.TotalSeconds));
            rate = activeSampleRate;

            CleanupUnmanaged();

            pcmData = recordedStream?.ToArray() ?? Array.Empty<byte>();
            recordedStream?.Dispose();
            recordedStream = null;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "PixelHelper", "voice");
        Directory.CreateDirectory(tempDir);
        currentFilePath = Path.Combine(tempDir, $"voice_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.wav");

        // Записываем правильный RIFF WAV файл (44 байта заголовок + PCM данные)
        WriteWavFile(currentFilePath, pcmData, rate);

        string waveformJson = GenerateWaveform(pcmData, 40);
        return (currentFilePath, durationSeconds, waveformJson);
    }

    public void Cancel()
    {
        lock (syncRoot)
        {
            if (!isRecording) return;
            isRecording = false;
            CleanupUnmanaged();
            recordedStream?.Dispose();
            recordedStream = null;
        }
    }

    private void CleanupUnmanaged()
    {
        if (hWaveIn != IntPtr.Zero)
        {
            try
            {
                waveInReset(hWaveIn);
                int headerSize = Marshal.SizeOf<WAVEHDR>();
                foreach (var pHdr in allocatedHeaders)
                {
                    waveInUnprepareHeader(hWaveIn, pHdr, headerSize);
                }
                waveInClose(hWaveIn);
            }
            catch { }
            finally
            {
                hWaveIn = IntPtr.Zero;
            }
        }

        foreach (var pHdr in allocatedHeaders)
        {
            try { Marshal.FreeHGlobal(pHdr); } catch { }
        }
        allocatedHeaders.Clear();

        foreach (var pBuf in allocatedBuffers)
        {
            try { Marshal.FreeHGlobal(pBuf); } catch { }
        }
        allocatedBuffers.Clear();
    }

    private static void WriteWavFile(string filePath, byte[] pcmData, uint sampleRate)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var bw = new BinaryWriter(fs);

        uint byteRate = sampleRate * 2; // 16-bit mono = 2 bytes per sample
        uint dataChunkSize = (uint)pcmData.Length;
        uint fileSize = 36 + dataChunkSize;

        // RIFF header
        bw.Write(new[] { 'R', 'I', 'F', 'F' });
        bw.Write(fileSize);
        bw.Write(new[] { 'W', 'A', 'V', 'E' });

        // fmt subchunk
        bw.Write(new[] { 'f', 'm', 't', ' ' });
        bw.Write((uint)16);         // Subchunk1Size (16 for PCM)
        bw.Write((ushort)1);        // AudioFormat (1 = PCM)
        bw.Write((ushort)1);        // NumChannels (1 = Mono)
        bw.Write(sampleRate);       // SampleRate
        bw.Write(byteRate);         // ByteRate
        bw.Write((ushort)2);        // BlockAlign
        bw.Write((ushort)16);       // BitsPerSample

        // data subchunk
        bw.Write(new[] { 'd', 'a', 't', 'a' });
        bw.Write(dataChunkSize);
        bw.Write(pcmData);
    }

    public static string GenerateWaveform(byte[] pcmData, int sampleCount = 40)
    {
        try
        {
            int totalSamples = pcmData.Length / 2;
            if (totalSamples <= 0) return "[]";

            int samplesPerChunk = Math.Max(1, totalSamples / sampleCount);
            var peaks = new List<double>();
            double maxRms = 0;

            for (int chunk = 0; chunk < sampleCount; chunk++)
            {
                long sumSquares = 0;
                int count = 0;
                int start = chunk * samplesPerChunk * 2;
                int end = Math.Min(pcmData.Length - 1, start + samplesPerChunk * 2);

                for (int pos = start; pos < end; pos += 2)
                {
                    short sample = (short)(pcmData[pos] | (pcmData[pos + 1] << 8));
                    sumSquares += (long)sample * sample;
                    count++;
                }

                double rms = count > 0 ? Math.Sqrt(sumSquares / (double)count) : 0;
                if (rms > maxRms) maxRms = rms;
                peaks.Add(rms);
            }

            var normalized = peaks.Select(p =>
            {
                if (maxRms <= 0) return 0.2;
                double val = Math.Round(Math.Clamp(p / maxRms, 0.1, 1.0), 2);
                return val;
            }).ToList();

            return JsonSerializer.Serialize(normalized);
        }
        catch
        {
            return "[]";
        }
    }

    public static string GenerateWaveform(string wavPath, int sampleCount = 40)
    {
        try
        {
            if (!File.Exists(wavPath)) return "[]";
            byte[] bytes = File.ReadAllBytes(wavPath);
            if (bytes.Length <= 44) return "[]";

            int dataOffset = 44;
            for (int i = 12; i < bytes.Length - 8; i++)
            {
                if (bytes[i] == 'd' && bytes[i + 1] == 'a' && bytes[i + 2] == 't' && bytes[i + 3] == 'a')
                {
                    dataOffset = i + 8;
                    break;
                }
            }

            byte[] pcm = new byte[bytes.Length - dataOffset];
            Array.Copy(bytes, dataOffset, pcm, 0, pcm.Length);
            return GenerateWaveform(pcm, sampleCount);
        }
        catch
        {
            return "[]";
        }
    }

    public void Dispose()
    {
        Cancel();
    }
}
