using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Identification
{
    public static class DistanceCalculator
    {
        // One artist of several is weaker evidence than the whole credit, so it only wins over a whole-credit miss of more than this
        private const double CreditedArtistPenalty = 0.2;

        // Keeps the credit as written ahead of its looser reading when both are candidates
        private const double LooseReadingPenalty = 0.01;

        // Keeps the credit as written ahead of the same credit with its featured guests left out
        private const double WithoutGuestsPenalty = 0.01;

        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(DistanceCalculator));

        public static readonly List<string> VariousArtistIds = new List<string> { "89ad4ac3-39f7-470e-963a-56509c546377" };
        private static readonly List<string> VariousArtistNames = new List<string> { "various artists", "various", "va", "unknown" };
        private static readonly Regex FeaturedArtists = new Regex(@"\s*[(\[]\s*(?:feat|ft|featuring)\b(?:\.\s*|\s+)(?=\w)[^)\]]*[)\]]?|(?:\s+|\s*[;/,&]\s*)(?:feat|ft|featuring)\b(?:\.\s*|\s+)(?=\w).*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Ampersand = new Regex(@"\s*&\s*", RegexOptions.Compiled);
        private static readonly Regex LeadingThe = new Regex(@"^\s*the\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ArtistSeparators = new Regex(@"\s*[;/,&]\s*", RegexOptions.Compiled);
        private static readonly Regex WebAddress = new Regex(@"^(?:[a-z][a-z0-9+.-]*://[^\s;,&]+|www\.[^\s;,&]+|[^\s/;,&]+\.(?:com|net|org|info|biz|ru|io)(?:/[^\s;,&]*)?)(?:\?[^\s;,]*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly List<IsoCountry> PreferredCountries = new List<string>
        {
            "United States",
            "United Kingdom",
            "Europe",
            "[Worldwide]"
        }.Select(x => IsoCountries.Find(x)).ToList();

        private static bool TrackIndexIncorrect(LocalTrack localTrack, Track mbTrack, int totalTrackNumber)
        {
            return localTrack.FileTrackInfo.TrackNumbers[0] != mbTrack.AbsoluteTrackNumber &&
                localTrack.FileTrackInfo.TrackNumbers[0] != totalTrackNumber;
        }

        // The name ahead of its aliases, so an artist named as tagged wins over another artist that has the name as an alias
        private static double NameOrAliasPenalty(string tag, List<string> names, List<string> aliases, Func<string, List<string>, double> penalty)
        {
            return aliases.Any() ? Math.Min(penalty(tag, names), penalty(tag, aliases) + Distance.AliasPenalty) : penalty(tag, names);
        }

        // "&" against "and", or a missing leading "The", costs short names more than CreditedArtistPenalty
        private static double WholeCreditPenalty(string tag, List<string> names)
        {
            return Math.Min(Distance.ClosestPenalty(tag, names), Distance.ClosestPenalty(LooseReading(tag), names.Select(LooseReading)) + LooseReadingPenalty);
        }

        private static string LooseReading(string name)
        {
            return LeadingThe.Replace(Ampersand.Replace(name ?? "", " and "), "");
        }

        // A web address in the artist tag is a watermark, not a name, unless the artist is named like one
        private static bool IsWatermark(string tag, List<string> names)
        {
            return IsWebAddress(tag) && !names.Any(IsWebAddress);
        }

        private static bool IsWebAddress(string name)
        {
            return WebAddress.IsMatch((name ?? "").Trim());
        }

        private static string WithoutGuests(string tag)
        {
            return FeaturedArtists.Replace(tag, "");
        }

        // A piece that cleans to nothing would match an artist whose name does too
        private static List<string> CreditedArtists(string tag)
        {
            var main = WithoutGuests(tag);
            var artists = ArtistSeparators.Split(main).Where(Distance.HasLettersOrDigits).ToList();

            return main != tag || artists.Count > 1 ? artists : new List<string>();
        }

        public static int GetTotalTrackNumber(Track track, List<Track> allTracks)
        {
            return track.AbsoluteTrackNumber + allTracks.Count(t => t.MediumNumber < track.MediumNumber);
        }

        public static Distance TrackDistance(LocalTrack localTrack, Track mbTrack, int totalTrackNumber, bool includeArtist = false)
        {
            var dist = new Distance();

            var localLength = localTrack.FileTrackInfo.Duration.TotalSeconds;
            var mbLength = mbTrack.Duration / 1000;
            var diff = Math.Abs(localLength - mbLength) - 10;

            if (mbLength > 0)
            {
                dist.AddRatio("track_length", diff, 30);
            }

            // musicbrainz never has 'featuring' in the track title
            // see https://musicbrainz.org/doc/Style/Artist_Credits
            dist.AddString("track_title", localTrack.FileTrackInfo.CleanTitle ?? "", mbTrack.Title);

            if (includeArtist && localTrack.FileTrackInfo.ArtistTitle.IsNotNullOrWhiteSpace()
                && !VariousArtistNames.Any(x => x.Equals(localTrack.FileTrackInfo.ArtistTitle, StringComparison.InvariantCultureIgnoreCase)))
            {
                dist.AddString("track_artist", localTrack.FileTrackInfo.ArtistTitle, mbTrack.ArtistMetadata.Value.Name);
            }

            if (localTrack.FileTrackInfo.TrackNumbers.FirstOrDefault() > 0 && mbTrack.AbsoluteTrackNumber > 0)
            {
                dist.AddBool("track_index", TrackIndexIncorrect(localTrack, mbTrack, totalTrackNumber));
            }

            var recordingId = localTrack.FileTrackInfo.RecordingMBId;
            if (recordingId.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("recording_id", localTrack.FileTrackInfo.RecordingMBId != mbTrack.ForeignRecordingId &&
                             !mbTrack.OldForeignRecordingIds.Contains(localTrack.FileTrackInfo.RecordingMBId));
            }

            // for fingerprinted files
            if (localTrack.AcoustIdResults != null)
            {
                dist.AddBool("recording_id", !localTrack.AcoustIdResults.Contains(mbTrack.ForeignRecordingId));
            }

            return dist;
        }

        public static Distance AlbumReleaseDistance(List<LocalTrack> localTracks, AlbumRelease release, TrackMapping mapping)
        {
            var dist = new Distance();

            var artist = localTracks.MostCommon(x => x.FileTrackInfo.ArtistTitle) ?? "";
            var metadata = release.Album.Value.ArtistMetadata.Value;
            var names = new List<string> { metadata.Name };
            var aliases = (metadata.Aliases ?? new List<string>()).Where(Distance.HasLettersOrDigits).ToList();

            // Only the name decides whether the tag is a watermark: another artist's release would skip it, so a web-like alias would lose
            if (!VariousArtistIds.Contains(metadata.ForeignArtistId) && !IsWatermark(artist, names))
            {
                // The credit without its guests is scored whole too, so a name such as "Earth, Wind & Fire" isn't only scored piece by piece
                var main = WithoutGuests(artist);
                var penalty = CreditedArtists(artist).Select(a => NameOrAliasPenalty(a, names, aliases, Distance.ClosestPenalty) + CreditedArtistPenalty)
                                                     .Append(NameOrAliasPenalty(artist, names, aliases, WholeCreditPenalty))
                                                     .Append(main != artist ? NameOrAliasPenalty(main, names, aliases, WholeCreditPenalty) + WithoutGuestsPenalty : 1.0)
                                                     .Min();
                dist.Add("artist", penalty);
                Logger.Trace("artist: {0} vs {1} or its {2} aliases; {3}", artist, metadata.Name, aliases.Count, dist.NormalizedDistance());
            }

            var title = localTracks.MostCommon(x => x.FileTrackInfo.AlbumTitle) ?? "";

            // Use the album title since the differences in release titles can cause confusion and
            // aren't always correct in the tags
            dist.AddString("album", title, release.Album.Value.Title);
            Logger.Trace("album: {0} vs {1}; {2}", title, release.Title, dist.NormalizedDistance());

            // Number of discs, either as tagged or the max disc number seen
            var discCount = localTracks.MostCommon(x => x.FileTrackInfo.DiscCount);
            discCount = discCount != 0 ? discCount : localTracks.Max(x => x.FileTrackInfo.DiscNumber);
            if (discCount > 0)
            {
                dist.AddNumber("media_count", discCount, release.Media.Count);
                Logger.Trace("media_count: {0} vs {1}; {2}", discCount, release.Media.Count, dist.NormalizedDistance());
            }

            // Media format
            if (release.Media.Select(x => x.Format).Contains("Unknown"))
            {
                dist.Add("media_format", 1.0);
            }

            // Year
            var localYear = localTracks.MostCommon(x => x.FileTrackInfo.Year);
            if (localYear > 0 && (release.Album.Value.ReleaseDate.HasValue || release.ReleaseDate.HasValue))
            {
                var albumYear = release.Album.Value.ReleaseDate?.Year ?? 0;
                var releaseYear = release.ReleaseDate?.Year ?? 0;
                if (localYear == albumYear || localYear == releaseYear)
                {
                    dist.Add("year", 0.0);
                }
                else
                {
                    var remoteYear = albumYear > 0 ? albumYear : releaseYear;
                    var diff = Math.Abs(localYear - remoteYear);
                    var diff_max = Math.Abs(DateTime.Now.Year - remoteYear);
                    dist.AddRatio("year", diff, diff_max);
                }

                Logger.Trace($"year: {localYear} vs {release.Album.Value.ReleaseDate?.Year} or {release.ReleaseDate?.Year}; {dist.NormalizedDistance()}");
            }

            // If we parsed a country from the files use that, otherwise use our preference
            var country = localTracks.MostCommon(x => x.FileTrackInfo.Country);
            if (release.Country.Count > 0)
            {
                if (country != null)
                {
                    dist.AddEquality("country", country.Name, release.Country);
                    Logger.Trace("country: {0} vs {1}; {2}", country.Name, string.Join(", ", release.Country), dist.NormalizedDistance());
                }
                else if (PreferredCountries.Count > 0)
                {
                    dist.AddPriority("country", release.Country, PreferredCountries.Select(x => x.Name).ToList());
                    Logger.Trace("country priority: {0} vs {1}; {2}", string.Join(", ", PreferredCountries.Select(x => x.Name)), string.Join(", ", release.Country), dist.NormalizedDistance());
                }
            }
            else
            {
                // full penalty if MusicBrainz release is missing a country
                dist.Add("country", 1.0);
            }

            var label = localTracks.MostCommon(x => x.FileTrackInfo.Label);
            if (label.IsNotNullOrWhiteSpace())
            {
                dist.AddEquality("label", label, release.Label);
                Logger.Trace("label: {0} vs {1}; {2}", label, string.Join(", ", release.Label), dist.NormalizedDistance());
            }

            var disambig = localTracks.MostCommon(x => x.FileTrackInfo.Disambiguation);
            if (disambig.IsNotNullOrWhiteSpace())
            {
                dist.AddString("album_disambiguation", disambig, release.Disambiguation);
                Logger.Trace("album_disambiguation: {0} vs {1}; {2}", disambig, release.Disambiguation, dist.NormalizedDistance());
            }

            var mbAlbumId = localTracks.MostCommon(x => x.FileTrackInfo.ReleaseMBId);
            if (mbAlbumId.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("album_id", mbAlbumId != release.ForeignReleaseId && !release.OldForeignReleaseIds.Contains(mbAlbumId));
                Logger.Trace("album_id: {0} vs {1} or {2}; {3}", mbAlbumId, release.ForeignReleaseId, string.Join(", ", release.OldForeignReleaseIds), dist.NormalizedDistance());
            }

            // tracks
            foreach (var pair in mapping.Mapping)
            {
                dist.Add("tracks", pair.Value.Item2.NormalizedDistance());
            }

            Logger.Trace("after trackMapping: {0}", dist.NormalizedDistance());

            // missing tracks
            foreach (var track in mapping.MBExtra.Take(localTracks.Count))
            {
                dist.Add("missing_tracks", 1.0);
            }

            Logger.Trace("after missing tracks: {0}", dist.NormalizedDistance());

            // unmatched tracks
            foreach (var track in mapping.LocalExtra.Take(localTracks.Count))
            {
                dist.Add("unmatched_tracks", 1.0);
            }

            Logger.Trace("after unmatched tracks: {0}", dist.NormalizedDistance());

            return dist;
        }
    }
}
