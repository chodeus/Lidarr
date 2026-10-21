# Fixed: Backend packages with security advisories

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`.

System.Security.Cryptography.Pkcs 8.0.2, pinned in `Lidarr.Core` over the 8.0.1 that MailKit 4.16.0 pulls in through MimeKit ([GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx), [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7)). The shipped projects (Lidarr, Lidarr.Update, Lidarr.Mono) then have no NuGet advisories left except System.Private.Uri 4.3.0, which contributes no files to the build.

Microsoft.AspNetCore.SignalR.Client 8.0.32 in the integration tests, over 8.0.27, whose SignalR.Common has [GHSA-334f-vvp4-7g6p](https://github.com/advisories/GHSA-334f-vvp4-7g6p). Only the tests use it.

Not changed, checked 2026-10-22: SixLabors.ImageSharp 3.1.12 has five advisories fixed only in 4.1.2 ([GHSA-j3p4-wp97-rph4](https://github.com/advisories/GHSA-j3p4-wp97-rph4), [GHSA-j9gm-c75j-xc9q](https://github.com/advisories/GHSA-j9gm-c75j-xc9q), [GHSA-jjfr-hcj7-qf5w](https://github.com/advisories/GHSA-jjfr-hcj7-qf5w), [GHSA-wmxv-xphr-5c9g](https://github.com/advisories/GHSA-wmxv-xphr-5c9g), [GHSA-gwg2-r3hj-4w44](https://github.com/advisories/GHSA-gwg2-r3hj-4w44)), and 4.x won't build without a Six Labors license key. Lidarr's only use, `ImageResizer`, registers just the JPEG, PNG and WebP decoders and decodes with `SkipMetadata`, so it reaches none of them: they are in the TIFF and BigTIFF codecs, histogram equalization, and ICC profile parsing. The test projects' System.Net.Http and System.Text.RegularExpressions 4.3.0 aren't shipped.

Drop this branch once develop resolves Pkcs 8.0.2 or later.
