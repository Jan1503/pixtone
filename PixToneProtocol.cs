using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace PixTone;

/// <summary>PixTone on UDP 7779. One datagram is 7.5 ms of 48 kHz stereo s16le.</summary>
public static class PixToneProtocol
{
    public const int Port = 7779;
    public const int WebPort = 5000;
    public const int FramesPerPacket = 360;
    public const int HeaderBytes = 16;
    public const int PcmBytes = FramesPerPacket * 4;
    public const double PacketMs = 1000.0 * FramesPerPacket / 48000.0;

    public const uint Magic = 0x4E4F5450;       // 'PTON'
    public const uint MagicVolume = 0x4C4F5656; // 'VVOL'
    public const uint MagicTitle = 0x54495456;  // 'VTIT'
    public const ushort FlagDiscontinuity = 0x0001;

    public static byte[] AudioPacket(uint seq, uint sampleIndex, ushort flags, ReadOnlySpan<byte> pcm)
    {
        var frames = pcm.Length / 4;
        var buf = new byte[HeaderBytes + frames * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), seq);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), sampleIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(12), flags);
        pcm[..(frames * 4)].CopyTo(buf.AsSpan(HeaderBytes));
        return buf;
    }

    public static byte[] VolumePacket(int volumePercent, bool mute)
    {
        var vol = (byte)Math.Clamp(volumePercent, 0, 100);
        return
        [
            0x56, 0x56, 0x4F, 0x4C,
            vol,
            mute ? (byte)1 : (byte)0
        ];
    }

    public static byte[] TitlePacket(string? title)
    {
        var text = title ?? "";
        var raw = Encoding.UTF8.GetBytes(text);
        if (raw.Length > 40) raw = raw[..40];
        var buf = new byte[4 + raw.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, MagicTitle);
        raw.CopyTo(buf.AsSpan(4));
        return buf;
    }

    public static IPEndPoint Endpoint(string host, int port = Port)
    {
        if (!IPAddress.TryParse(host, out var ip))
            throw new ArgumentException($"Not an IPv4 address: {host}", nameof(host));
        return new IPEndPoint(ip, port);
    }
}
