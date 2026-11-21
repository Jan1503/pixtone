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
    public const uint MagicClock = 0x4B4C4356;  // 'VCLK'
    public const uint MagicPicture = 0x43495056; // 'VPIC'
    public const uint MagicCover = 0x564F4356; // 'VCOV'
    public const int CoverSize = 80;
    public const ushort FlagDiscontinuity = 0x0001;
    public const int PreviewWidth = 160;
    public const int PreviewHeight = 120;
    public const int PreviewWidthMax = 480;
    public const int PreviewHeightMax = 160;
    public const int PictureChunkBytes = 960;
    public const int PictureChunkBytesFast = 1440;

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
        if (raw.Length > 96) raw = raw[..96];
        var buf = new byte[4 + raw.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, MagicTitle);
        raw.CopyTo(buf.AsSpan(4));
        return buf;
    }

    public static byte[] ClockPacket(int hour, int minute, int second)
    {
        return
        [
            0x56, 0x43, 0x4C, 0x4B,
            (byte)hour, (byte)minute, (byte)second
        ];
    }

    /// <summary>
    ///     One piece of a preview frame. Only the rows that contain the wall are sent;
    ///     <paramref name="originY"/> is where they sit, and <paramref name="width"/> is the row stride.
    /// </summary>
    public static byte[] PictureChunk(byte frame, byte index, byte count, byte originY, byte rows,
        ushort width, ushort height, ushort chunkStride, ReadOnlySpan<byte> rgb565)
    {
        var n = Math.Min(rgb565.Length, chunkStride);
        var buf = new byte[16 + n];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, MagicPicture);
        buf[4] = frame;
        buf[5] = index;
        buf[6] = count;
        buf[7] = originY;
        buf[8] = rows;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(9), chunkStride);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(12), width);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(14), height);
        rgb565[..n].CopyTo(buf.AsSpan(16));
        return buf;
    }

    /// <summary>One piece of an 80×80 RGB565 cover. A count of zero clears the slot.</summary>
    public static byte[] CoverChunk(byte index, byte count, ReadOnlySpan<byte> rgb565)
    {
        var n = Math.Min(rgb565.Length, PictureChunkBytes);
        var buf = new byte[8 + n];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, MagicCover);
        buf[4] = index;
        buf[5] = count;
        buf[6] = CoverSize;
        if (n > 0) rgb565[..n].CopyTo(buf.AsSpan(8));
        return buf;
    }

    public static byte[] CoverClear() => CoverChunk(0, 0, ReadOnlySpan<byte>.Empty);

    public static IPEndPoint Endpoint(string host, int port = Port)
    {
        if (!IPAddress.TryParse(host, out var ip))
            throw new ArgumentException($"Not an IPv4 address: {host}", nameof(host));
        return new IPEndPoint(ip, port);
    }
}
