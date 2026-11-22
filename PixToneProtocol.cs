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
    public const uint MagicFill = 0x54494656;  // 'VFIT' center crop on the 3.5" glass
    public const uint MagicPlay = 0x594C5056;  // 'VPLY' host play state, for the panel's transport row
    public const uint MagicKey = 0x59454B56;   // 'VKEY' panel button, board to host
    public const uint MagicSettings = 0x54455356; // 'VSET' shuffle, repeat, wall, sleep, A/V sync
    public const uint MagicList = 0x54534C56;  // 'VLST' one page of a panel list
    public const uint MagicSpectrum = 0x43505356; // 'VSPC' band levels, board to host
    public const int ListNameBytes = 28;
    public const int ListMax = 64;
    public const int ListChunk = 40;
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

    /// <summary>1 asks the 3.5" board to show the center of the wall at 2×. The 2.8" board ignores it.</summary>
    public static byte[] FillPacket(bool on) => [0x56, 0x46, 0x49, 0x54, on ? (byte)1 : (byte)0];

    /// <summary>
    ///     What the host is playing, about once a second and on every change. The board sends
    ///     <c>VKEY</c> back to the address and port this came from, so it works through NAT.
    /// </summary>
    public static byte[] PlayPacket(PlayState state, PlayFlags flags, uint positionMs, uint durationMs)
    {
        var buf = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, MagicPlay);
        buf[4] = (byte)state;
        buf[5] = (byte)flags;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), positionMs);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12), durationMs);
        return buf;
    }

    /// <summary>
    ///     A panel button: 'VKEY', key u8, sequence u8, argument u16, list generation u8, pad u8.
    ///     Seek is per mille, sync is ms + 2000, select is list &lt;&lt; 12 | index.
    ///     The board sends each press three times with the same sequence.
    /// </summary>
    public static bool TryParseKey(ReadOnlySpan<byte> datagram, out PanelKey key, out byte sequence, out ushort argument)
        => TryParseKey(datagram, out key, out sequence, out argument, out _);

    public static bool TryParseKey(ReadOnlySpan<byte> datagram, out PanelKey key, out byte sequence,
        out ushort argument, out byte listGeneration)
    {
        key = PanelKey.None;
        sequence = 0;
        argument = 0;
        listGeneration = 0;
        if (datagram.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(datagram) != MagicKey) return false;
        key = (PanelKey)datagram[4];
        sequence = datagram[5];
        argument = BinaryPrimitives.ReadUInt16LittleEndian(datagram[6..]);
        if (datagram.Length >= 9) listGeneration = datagram[8];
        return key != PanelKey.None;
    }

    /// <summary>
    ///     Panel settings: flags, wall brightness 0–100, sleep minutes left, A/V sync in ms, and the
    ///     panel's alarm: minute of the day (-1 none) and weekdays (bit 0 Monday … bit 6 Sunday, 0 once).
    /// </summary>
    public static byte[] SettingsPacket(PanelFlags flags, int brightness, int sleepMinutes, int syncMs,
        int alarmMinute = -1, int alarmDays = 0)
    {
        var buf = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, MagicSettings);
        buf[4] = (byte)flags;
        buf[5] = (byte)Math.Clamp(brightness, 0, 100);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(6), (ushort)Math.Clamp(sleepMinutes, 0, 65535));
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(8), (short)Math.Clamp(syncMs, -2000, 2000));
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(10),
            alarmMinute is >= 0 and < 1440 ? (ushort)alarmMinute : (ushort)0xFFFF);
        buf[12] = (byte)(alarmDays & 0x7F);
        return buf;
    }

    /// <summary>'VSPC', sequence u8, count u8, then count levels 0–255 (low to high frequency).</summary>
    public static bool TryParseSpectrum(ReadOnlySpan<byte> datagram, out float[] levels)
    {
        levels = Array.Empty<float>();
        if (datagram.Length < 7 || BinaryPrimitives.ReadUInt32LittleEndian(datagram) != MagicSpectrum) return false;
        int count = datagram[5];
        if (count == 0 || datagram.Length < 6 + count) return false;
        levels = new float[count];
        for (var i = 0; i < count; i++) levels[i] = datagram[6 + i] / 255f;
        return true;
    }

    /// <summary>
    ///     A list for the panel as datagrams of up to <see cref="ListChunk"/> names (UTF-8, cut to
    ///     27 bytes on a character boundary). <paramref name="active"/> is the playing entry or -1.
    /// </summary>
    public static IEnumerable<byte[]> ListPackets(PanelList list, byte generation, IReadOnlyList<string> names, int active)
    {
        var total = Math.Min(names.Count, ListMax);
        var first = 0;
        do
        {
            var count = Math.Min(ListChunk, total - first);
            var buf = new byte[12 + count * ListNameBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(buf, MagicList);
            buf[4] = (byte)list;
            buf[5] = generation;
            buf[6] = (byte)total;
            buf[7] = (byte)first;
            buf[8] = (byte)count;
            buf[9] = active is >= 0 and < ListMax ? (byte)active : (byte)0xFF;
            for (var i = 0; i < count; i++)
            {
                var raw = Utf8Cut(names[first + i], ListNameBytes - 1);
                raw.CopyTo(buf.AsSpan(12 + i * ListNameBytes));
            }
            yield return buf;
            first += count;
        } while (first < total);
    }

    private static byte[] Utf8Cut(string? text, int maxBytes)
    {
        var raw = Encoding.UTF8.GetBytes((text ?? "").Trim());
        if (raw.Length <= maxBytes) return raw;
        var n = maxBytes;
        while (n > 0 && (raw[n] & 0xC0) == 0x80) n--;
        return raw[..n];
    }

    public static IPEndPoint Endpoint(string host, int port = Port)
    {
        if (!IPAddress.TryParse(host, out var ip))
            throw new ArgumentException($"Not an IPv4 address: {host}", nameof(host));
        return new IPEndPoint(ip, port);
    }
}

public enum PlayState : byte
{
    Stopped = 0,
    Playing = 1,
    Paused = 2
}

[Flags]
public enum PlayFlags : byte
{
    None = 0,
    CanSeek = 1,
    HasPrevious = 2,
    HasNext = 4
}

public enum PanelKey : byte
{
    None = 0,
    PlayPause = 1,
    Stop = 2,
    Previous = 3,
    Next = 4,
    /// <summary>Argument is the target in per mille of the duration.</summary>
    Seek = 5,
    Shuffle = 6,
    Repeat = 7,
    /// <summary>Steps the sleep timer: off, 15, 30, 60, 90 minutes.</summary>
    Sleep = 8,
    /// <summary>Argument is the A/V offset in ms plus 2000.</summary>
    Sync = 9,
    /// <summary>Argument is the wall brightness in percent.</summary>
    Brightness = 10,
    WallPower = 11,
    /// <summary>Argument is list &lt;&lt; 12 | index; the list generation travels with it.</summary>
    Select = 12,
    /// <summary>Wall visualizer on or off, fed by the board's spectrum.</summary>
    Visualizer = 13,
    /// <summary>The panel's alarm on or off.</summary>
    Alarm = 14,
    /// <summary>Argument is the alarm time in minutes after midnight.</summary>
    AlarmTime = 15,
    /// <summary>Remember the song that is playing.</summary>
    SaveTitle = 16
}

[Flags]
public enum PanelFlags : byte
{
    None = 0,
    Shuffle = 1,
    Repeat = 2,
    WallOn = 4,
    Radio = 8,
    /// <summary>Music or radio without a picture: the panel's screensaver shows cover and spectrum.</summary>
    AudioOnly = 16,
    /// <summary>The wall visualizer runs on the board's spectrum: send 'VSPC'.</summary>
    Visualizer = 32,
    /// <summary>The panel's alarm is on.</summary>
    Alarm = 64
}

public enum PanelList : byte
{
    Favorites = 1,
    Radio = 2,
    Layouts = 3
}
