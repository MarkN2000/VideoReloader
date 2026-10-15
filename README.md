# VideoReloader

[![Thunderstore](https://img.shields.io/badge/Thunderstore-Resonite-5c81f7)](https://thunderstore.io/c/resonite/)

![VideoReloader](https://raw.githubusercontent.com/MarkN2000/VideoReloader/main/icon.png)

A [Resonite](https://resonite.com/) mod that reloads videos in your current session on your client.

Retry failed video loading after changing streaming settings without duplicating players.

In Bepis Mod Settings, open **VideoReloader > Reloading**:

- **Reload Session Videos (Local)** reloads the current session's videos manually.
- **Auto-Reload on Settings Change** reloads videos after changing the cookie browser or Preferred Resolution under Video Streaming Services. It is **enabled by default**, with a **1-second delay** after the last change.

Reloading briefly interrupts video and audio on your client. The mod leaves shared URLs and playback controls unchanged. Switching sessions cancels pending reloads.

## Installation

1. Install [BepisLoader](https://thunderstore.io/c/resonite/p/ResoniteModding/BepisLoader/).
2. Download the ZIP from [Releases](https://github.com/MarkN2000/VideoReloader/releases) and import it into Gale.

The package declares BepInExResoniteShim, BepisResoniteWrapper, and BepisModSettings as dependencies.

[markn's homepage](https://markn2000.com/posts/2025-09-08-resonite-mod-list/)
