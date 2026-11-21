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

`VVOL` sets volume 0–100 and mute. The board keeps its own saved level and only applies `VVOL` in RAM. `VTIT` sets a short title. `VCLK` is hour, minute, second in local time. `VPIC` is a wall-preview chunk (160-wide frames use 960-byte pieces so the 2.8" firmware can assemble them; wider frames use 1440). `VCOV` is an 80×80 RGB565 cover, or a count of 0 to clear it. The 3.5" firmware draws that cover; the 2.8" firmware scales it to 40×40. The board answers every 100 ms with fill level, underruns and overruns. Packet *n* is due at the first send plus *n* × 7.5 ms, so a late wakeup does not slow the stream down. `PauseClock` freezes that schedule while a movie is paused.

```powershell
dotnet build PixTone.csproj -c Release
```

verpixeld references this project as a sibling (`../pixtone`). Clone it next to `verpixeld` and `pixplane`:

```bash
git clone https://github.com/Jan1503/pixtone.git pixtone
```

## License

MIT. See [LICENSE](LICENSE).
