# PixTone

Host-side sender for a verpixeld audio board. The library does not start FFmpeg and has no UI. The host feeds 48 kHz stereo s16le; this paces it onto UDP.

The board is a second RP2350 with Ethernet, next to the LED panel. Discovery is `GET /status` on TCP 5000 (`kind` must be `verpixeld-audio`). Audio is UDP **7779**, not the panel port 7777.

One datagram is 7.5 ms:

| Field | Type |
|-------|------|
| magic | `PTON` |
| sequence | u32 |
| sample index | u32 |
| flags | u16, bit 0 = discontinuity |
| reserved | u16 |
| PCM | 360 frames, s16le stereo |

`VVOL` sets volume 0–100 and mute. The board keeps its own saved level and only applies `VVOL` in RAM. `VTIT` sets a short title. `VCLK` is hour, minute, second in local time. `VPIC` is a wall-preview chunk (160-wide frames use 960-byte pieces so the 2.8" firmware can assemble them; wider frames use 1440). A board that reports `"pic":<port>` in `/status` takes `VPIC` on that port (the 3.5" board: 7780) and keeps PCM alone on 7779. `AudioAnnouncement.PicturePort` is 0 for boards without it. `VPLY` is the host's play state for the panel: state (stopped, playing, paused), flags (can seek, has previous, has next), position and duration in ms. The board answers a panel button with `VKEY` (key, sequence, argument; seek is per mille) to the address and port the last `VPLY` came from, three times with one sequence. `PixToneSender.ReceiveAsync` reads it and `PixToneProtocol.TryParseKey` decodes it. Keys 6–12 are shuffle, repeat, sleep, A/V sync (argument ms + 2000), wall brightness (percent), wall on/off and list select (argument list << 12 | index, with the list generation in byte 8). `VSET` (`SettingsPacket`) carries shuffle, repeat, wall on, radio, audio only, wall visualizer, alarm on, brightness, sleep minutes, A/V sync, and the panel alarm's time and weekdays. Keys 13–16 are wall visualizer, alarm on/off, alarm time (minutes after midnight) and remember song. With the visualizer flag set the board sends `VSPC` (sequence, count, levels 0–255); `TryParseSpectrum` reads it. `VLST` (`ListPackets`) sends a list (1 favorites, 2 radio, 3 layouts) in pieces of 40 names of 28 bytes, with a generation and the active entry. `VCOV` is an 80×80 RGB565 cover, or a count of 0 to clear it. The 3.5" firmware draws that cover; the 2.8" firmware scales it to 40×40. The board answers every 100 ms with fill level, underruns and overruns. `AudioAnnouncement.Uptime` is the board's `up` from `/status`; verpixeld sends the title and cover again when it drops (the board restarted) or the board comes back after being unreachable, and repeats the title every 15 seconds. Packet *n* is due at the first send plus *n* × 7.5 ms, so a late wakeup does not slow the stream down. `PauseClock` freezes that schedule while a movie is paused.

```powershell
dotnet build PixTone.csproj -c Release
```

verpixeld references this project as a sibling (`../pixtone`). Clone it next to `verpixeld` and `pixplane`:

```bash
git clone https://github.com/Jan1503/pixtone.git pixtone
```

## License

MIT. See [LICENSE](LICENSE).
