# Fixed: Backend packages with security advisories

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`.

System.Security.Cryptography.Pkcs 8.0.2, pinned in `Lidarr.Core` over the 8.0.1 that MailKit 4.16.0 pulls in through MimeKit ([GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx), [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7)). The shipped projects (Lidarr, Lidarr.Update, Lidarr.Mono) then have no NuGet advisories left except System.Private.Uri 4.3.0, which contributes no files to the build.

Drop this branch once develop resolves Pkcs 8.0.2 or later.
