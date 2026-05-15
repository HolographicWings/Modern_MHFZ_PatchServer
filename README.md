
# Modern MHFZ Patch Server

Modern MHFZ Patch Server is a lightweight C# patch server for Monster Hunter Frontier Z launcher environments.

This specific adaptation is built for the launcher i plan to make in future, with the support of Game Package's customization.

It is inspired from [`mrsasy89/MHFZ-Patch-Server`](https://github.com/mrsasy89/MHFZ-Patch-Server), with a stronger focus on configuration, packages/versions, manifests, persistence/caching, and a small built-in administration endpoint.

> This project provides the server-side patch delivery layer only. It does not include Monster Hunter Frontier Z game files or any Capcom copyrighted content.

## Features

### Legacy
- Serves game files over HTTP as it's predecessors.
- Generates launcher manifests as it's predecessors.
- Provides `ButterVersion.txt` endpoints for version checks as it's predecessors.
- Supports raw file hosting through the `Files/` directory as it's predecessors.
- Provides configurable HTTP listener, port, client limit, and bandwidth limit.
- Supports legacy manifest format (`check` endpoint)
- Provides configurable HTTP port and client limit.
### New Generation
- Supports optional game packages with multiple versions.
- Computes checksums in multithread.
- Supports new manifest format (`check2` endpoint)
- Supports persistent checksum caching for faster bootup.
- Provides configurable HTTP listener, and bandwidth limit.
- Adds basic security headers to HTTP responses.
- Includes a minimal admin panel protected by HTTP Basic authentication.

## Roadmap

- HTTPS Support.
- True External WebServer support.
- Status page endpoint.
- Supports console commands.

## Documentation

Full documentation will be available in the [Wiki](https://github.com/HolographicWings/Modern_MHFZ_PatchServer/wiki). (Not written yet)

Recommended reading order:

1. To be written...

## Quick start

Build or publish the server, open the `config.json` configuration file to personalize the Server's behavior, and put game files under the configured game root.

Default layout:

```text
Modern_MHFZ_PatchServer/
├─ Modern_MHFZ_PatchServer.exe
├─ config.json
└─ Game/
   └─ Base/
      └─ v1.0/
         ├─ dat/
         ├─ mhfo.dll
         ├─ mhfo-hd.dll
         ├─ mhf.ini
         └─ ...
```

Default server URL:

```text
http://127.0.0.1:8094/
```

Common launcher endpoints:

```text
/check
/check2
/ButterVersion.txt
```

## Minimal configuration

```json
{
  "GameData": {
    "ChecksumThreads": 4,
    "PersistentChecksum": true,
    "RootFolder": "",
    "BasePackageCurrentVersion": "v1.0",
    "GamePackages": []
  },
  "WebServer": {
    "Enabled": true,
    "Port": 8094,
    "Listener": "127.0.0.1",
    "MaxClients": 5,
    "BandwidthLimit": "20M",
    "AdminPanel": {
      "Enabled": true,
      "Username": "admin",
      "Password": "change-me"
    }
  },
  "Logger": {
    "WriteLog": true,
    "Debug": false,
    "PuTTYMode": false
  }
}
```

Please change the default admin password before exposing the server to anything more dangerous than your Local Area Network. Humanity already invented enough avoidable disasters.

## Project status

This project is still not Feature Complete. Some parts are intentionally minimal:

- `/status` currently returns a placeholder response.
- The admin panel command system exists, but available commands are limited to `Stop`.
- The admin panel uses HTTP Basic authentication and must be protected by HTTPS and network restrictions in production.
- The native HTTPS support is TBD.
- External WebServer is theorically functional, but might require custom rules for your webserver to provide the proper ETag in the `check` & `check2` endpoints.

See [Development notes](docs/development.md) and [Troubleshooting](docs/troubleshooting.md) for implementation notes and known pitfalls.

## Credits

- Modern MHFZ Patch Server inspired by [`mrsasy89/MHFZ-Patch-Server`](https://github.com/mrsasy89/MHFZ-Patch-Server) itself inspired by [`rockisch/mhf-launcher`](https://github.com/rockisch/mhf-launcher).
- Launcher protocol originally developed for ButterClient by [LilButter](https://github.com/LilButter).

## License

This project is licensed under the MIT License.

See the [LICENSE](https://github.com/HolographicWings/Modern_MHFZ_PatchServer/blob/master/LICENSE) file for details.
