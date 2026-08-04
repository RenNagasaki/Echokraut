namespace Echokraut.Helper.Functional;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public static class RawPcmToWav
{
    public static async Task CreateWaveFileAsync(
        string filePath,
        Stream pcmStream,
        int sampleRate = 24000,
        short bitsPerSample = 16,
        short channels = 1,
        CancellationToken ct = default)
    {
        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await WriteWavAsync(pcmStream, fs, sampleRate, bitsPerSample, channels, ct);
    }

    public static void CreateWaveFile(
        string filePath,
        Stream pcmStream,
        int sampleRate = 24000,
        short bitsPerSample = 16,
        short channels = 1)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: false);
        WriteWav(pcmStream, fs, sampleRate, bitsPerSample, channels);
    }

    public static async Task WriteWavAsync(
        Stream pcmStream,
        Stream output,
        int sampleRate,
        short bitsPerSample,
        short channels,
        CancellationToken ct = default)
    {
        if (!output.CanSeek) throw new NotSupportedException("Output stream must be seekable to finalize WAV header.");

        // Falls der Input bereits gelesen wurde, an den Anfang setzen (optional)
        if (pcmStream.CanSeek) pcmStream.Seek(0, SeekOrigin.Begin);

        short blockAlign = (short)(channels * (bitsPerSample / 8));
        int byteRate = sampleRate * blockAlign;

        using var bw = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);

        // Header (Platzhalter)
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(0); // RIFF size placeholder
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);               // PCM fmt chunk size
        bw.Write((short)1);         // AudioFormat = 1 (PCM)
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(0);                // data size placeholder

        // Daten kopieren
        var buffer = new byte[81920];
        long dataBytes = 0;
        int read;
        while ((read = await pcmStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            dataBytes += read;
        }
        await output.FlushAsync(ct);

        // Größen patchen
        long endPos = output.Position;
        output.Seek(4, SeekOrigin.Begin);
        bw.Write((int)(36 + dataBytes)); // RIFF size
        output.Seek(40, SeekOrigin.Begin);
        bw.Write((int)dataBytes);        // data chunk size
        output.Seek(endPos, SeekOrigin.Begin);
        await output.FlushAsync(ct);
    }

    public static void WriteWav(
        Stream pcmStream,
        Stream output,
        int sampleRate,
        short bitsPerSample,
        short channels)
    {
        if (!output.CanSeek) throw new NotSupportedException("Output stream must be seekable to finalize WAV header.");
        if (pcmStream.CanSeek) pcmStream.Seek(0, SeekOrigin.Begin);

        short blockAlign = (short)(channels * (bitsPerSample / 8));
        int byteRate = sampleRate * blockAlign;

        using var bw = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);

        // Header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(0);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(0);

        // Daten
        var buffer = new byte[81920];
        long dataBytes = 0;
        int read;
        while ((read = pcmStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            dataBytes += read;
        }
        output.Flush();

        // Patchen
        long endPos = output.Position;
        output.Seek(4, SeekOrigin.Begin);
        bw.Write((int)(36 + dataBytes));
        output.Seek(40, SeekOrigin.Begin);
        bw.Write((int)dataBytes);
        output.Seek(endPos, SeekOrigin.Begin);
        output.Flush();
    }
}

