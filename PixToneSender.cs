using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

namespace PixTone;

/// <summary>
///     Sends PixTone datagrams to one board. The caller feeds 48 kHz stereo s16le.
///     Packet n is due at the first send plus n × 7.5 ms. A late wakeup does not move
///     that schedule, so the average rate stays 48 kHz instead of slowing down.
/// </summary>
public sealed class PixToneSender : IDisposable
{
    private readonly UdpClient _udp = new();
    private readonly System.Net.IPEndPoint _remote;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _clockGate = new();
    private TimeSpan _skipped;
    private TimeSpan? _heldAt;

    public void PauseClock()
    {
        lock (_clockGate)
        {
            if (_heldAt == null) _heldAt = _clock.Elapsed;
        }
    }

    public void ResumeClock()
    {
        lock (_clockGate)
        {
            if (_heldAt == null) return;
            _skipped += _clock.Elapsed - _heldAt.Value;
            _heldAt = null;
        }
    }

    private TimeSpan MediaNow
    {
        get
        {
            lock (_clockGate)
            {
                var now = _heldAt ?? _clock.Elapsed;
                return now - _skipped;
            }
        }
    }

    private bool IsHeld
    {
        get { lock (_clockGate) return _heldAt != null; }
    }

    public bool IsPaused => IsHeld;
    private uint _seq;
    private uint _sampleIndex;
    private bool _discontinuity = true;
    private TimeSpan? _origin;
    private int _sent;

    public PixToneSender(string host, int port = PixToneProtocol.Port)
    {
        _remote = PixToneProtocol.Endpoint(host, port);
    }

    public void SetVolume(int volumePercent, bool mute) => Send(PixToneProtocol.VolumePacket(volumePercent, mute));

    public void SetTitle(string? title) => Send(PixToneProtocol.TitlePacket(title));

    public async Task PumpAsync(Stream pcm, CancellationToken ct = default, Action? onFirstPacket = null)
    {
        var packet = new byte[PixToneProtocol.PcmBytes];
        TimeSpan? origin = null;
            var sent = 0;
            while (!ct.IsCancellationRequested)
            {
                var n = 0;
                while (n < packet.Length)
                {
                    var read = await pcm.ReadAsync(packet.AsMemory(n, packet.Length - n), ct).ConfigureAwait(false);
                    if (read == 0)
                    {
                        if (n >= 8) SendPcm(packet.AsSpan(0, n & ~3));
                        return;
                    }
                    n += read;
                }

                if (origin == null)
                {
                    SendPcm(packet);
                    origin = MediaNow;
                    onFirstPacket?.Invoke();
                    sent = 1;
                    continue;
                }

                var due = origin.Value + TimeSpan.FromMilliseconds(sent * PixToneProtocol.PacketMs);
                if (MediaNow < due)
                    await WaitUntil(due, ct).ConfigureAwait(false);
                while (IsHeld)
                    await Task.Delay(20, ct).ConfigureAwait(false);
                SendPcm(packet);
                sent++;
            }
    }

    /// <summary>Send one PCM packet on the shared 48 kHz schedule. Blocks until it is due.</summary>
    public void Submit(byte[] pcm)
    {
        while (IsHeld) Thread.Sleep(20);
        if (_origin == null)
        {
            SendPcm(pcm);
            _origin = MediaNow;
            _sent = 1;
            return;
        }

        var due = _origin.Value + TimeSpan.FromMilliseconds(_sent * PixToneProtocol.PacketMs);
        while (MediaNow < due)
        {
            if (IsHeld) { Thread.Sleep(20); continue; }
            var left = due - MediaNow;
            if (left > TimeSpan.FromMilliseconds(2)) Thread.Sleep(1);
            else Thread.SpinWait(40);
        }
        SendPcm(pcm);
        _sent++;
    }

    public void Dispose() => _udp.Dispose();

    private async Task WaitUntil(TimeSpan due, CancellationToken ct)
    {
        while (MediaNow < due)
        {
            ct.ThrowIfCancellationRequested();
            if (IsHeld)
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
                continue;
            }
            var left = due - MediaNow;
            if (left > TimeSpan.FromMilliseconds(12))
                await Task.Delay(8, ct).ConfigureAwait(false);
            else
                Thread.SpinWait(40);
        }
    }

    private void SendPcm(ReadOnlySpan<byte> pcm)
    {
        var flags = _discontinuity ? PixToneProtocol.FlagDiscontinuity : (ushort)0;
        _discontinuity = false;
        var frames = (uint)(pcm.Length / 4);
        Send(PixToneProtocol.AudioPacket(_seq++, _sampleIndex, flags, pcm));
        _sampleIndex += frames;
    }

    private void Send(byte[] datagram)
    {
        lock (_gate)
            _udp.Send(datagram, datagram.Length, _remote);
    }
}
