# Phosphor

**Phosphor** is a classic CRT monitor viewer designed for retro hardware capture
(SCART → USB, composite, RGB adapters, etc).

It focuses on *authentic CRT behaviour* rather than post-processing gimmicks.

---

## Features

- Real-time CRT shader (HLSL)
- Phosphor mask & scanlines
- Geometry correction (horizontal / vertical size)
- Gamma, brightness, contrast, saturation controls
- Live audio monitoring (NAudio)
- Persistent per-user settings
- Designed for retro computers & consoles

---

## Screenshots

### ZX Spectrum – Loader Screen
![ZX Spectrum Loader](/RetroDisplay/Screenshots/main-zxspectrum.png)

### Renegade – No Filters
![Renegade No Filters](/RetroDisplay/Screenshots/zxspectrum-renagade-nofilters.png)

### Renegade – CRT Filters Enabled
![Renegade Filters](/RetroDisplay/Screenshots/zxspectrum-renagade-filters.png)

### Renegade – Title Screen (No Filters)
![Renegade Title No Filters](/RetroDisplay/Screenshots/zxspectrum-renagade-title-nofilters.png)

### Renegade – Title Screen (CRT Filters)
![Renegade Title Filters](/RetroDisplay/Screenshots/zxspectrum-renagade-title-filters.png)

---

## Status

**v1.0** – Initial release  
Actively developed.

---

## Tech

- WPF (.NET)
- AForge.Video.DirectShow
- NAudio (WASAPI)
- Custom HLSL CRT shader

---

## License

TBD
