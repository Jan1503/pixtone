# VAP1

Host-side sender for a verpixeld audio board. The library does not start FFmpeg and has no UI. The host feeds 48 kHz stereo s16le; this paces it onto UDP.

The board is a second RP2350 with Ethernet, next to the LED panel. Discovery is `GET /status` on TCP 5000 (`kind` must be `verpixeld-audio`). Audio is UDP **7779**, not the panel port 7777.

One datagram is 7.5 ms:

| Field | Type |
|-------|------|
| magic | `VAP1` |
| sequence | u32 |
| sample index | u32 |
| flags | u16, bit 0 = discontinuity |
| reserved | u16 |
| PCM | 360 frames, s16le stereo |

`VVOL` sets volume 0–100 and mute. `VTIT` sets a short title. The board answers every 100 ms with fill level, underruns and overruns. Packet *n* is due at the first send plus *n* × 7.5 ms, so a late wakeup does not slow the stream down. `PauseClock` freezes that schedule while a movie is paused.

```powershell
dotnet build Vap1.csproj -c Release
```

verpixeld references this project as a sibling (`../vap1`). Clone it next to `verpixeld` and `pixplane`:

```bash
git clone https://github.com/Jan1503/vap1.git vap1
```

## License

MIT. See [LICENSE](LICENSE).
