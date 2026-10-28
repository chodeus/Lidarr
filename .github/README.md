# Fixed: Backend packages with security advisories

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`.

System.Security.Cryptography.Pkcs 8.0.2, pinned in `Lidarr.Core` over the 8.0.1 that MailKit 4.16.0 pulls in through MimeKit ([GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx), [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7)). The shipped projects (Lidarr, Lidarr.Update, Lidarr.Mono) then have no NuGet advisories left except ImageSharp's, below (checked 2026-10-28).

Microsoft.AspNetCore.SignalR.Client 8.0.33 in the integration tests, over 8.0.27, whose SignalR.Common has [GHSA-334f-vvp4-7g6p](https://github.com/advisories/GHSA-334f-vvp4-7g6p). Only the tests use it.

Not changed, checked 2026-10-28: SixLabors.ImageSharp 3.1.12. Its advisories are fixed only in 4.x, which won't build without a Six Labors license key. Develop suppresses all six in `Directory.Build.props`, since its `ImageResizer`, the only use, registers just the JPEG, PNG and WebP decoders and decodes with `SkipMetadata`. The test projects' System.Net.Http and System.Text.RegularExpressions 4.3.0 aren't shipped.

Drop this branch once develop resolves Pkcs 8.0.2 or later.
