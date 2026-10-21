using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Notifications.Gotify
{
    public class Gotify : NotificationBase<GotifySettings>
    {
        private readonly IGotifyProxy _proxy;
        private readonly Logger _logger;

        public Gotify(IGotifyProxy proxy, Logger logger)
        {
            _proxy = proxy;
            _logger = logger;
        }

        public override string Name => "Gotify";
        public override string Link => "https://gotify.net/";

        public override void OnGrab(GrabMessage grabMessage)
        {
            SendNotification(ALBUM_GRABBED_TITLE, grabMessage.Message, grabMessage.Artist, grabMessage.RemoteAlbum?.Albums);
        }

        public override void OnReleaseImport(AlbumDownloadMessage message)
        {
            SendNotification(ALBUM_DOWNLOADED_TITLE, message.Message, message.Artist, message.Album == null ? null : new List<Album> { message.Album });
        }

        public override void OnArtistAdd(ArtistAddMessage message)
        {
            SendNotification(ARTIST_ADDED_TITLE, message.Message, message.Artist);
        }

        public override void OnArtistDelete(ArtistDeleteMessage deleteMessage)
        {
            SendNotification(ARTIST_DELETED_TITLE, deleteMessage.Message, deleteMessage.Artist);
        }

        public override void OnAlbumDelete(AlbumDeleteMessage deleteMessage)
        {
            SendNotification(ALBUM_DELETED_TITLE, deleteMessage.Message, deleteMessage.Album?.Artist, deleteMessage.Album == null ? null : new List<Album> { deleteMessage.Album });
        }

        public override void OnHealthIssue(HealthCheck.HealthCheck healthCheck)
        {
            SendNotification(HEALTH_ISSUE_TITLE, healthCheck.Message, null);
        }

        public override void OnHealthRestored(HealthCheck.HealthCheck previousCheck)
        {
            SendNotification(HEALTH_RESTORED_TITLE, $"The following issue is now resolved: {previousCheck.Message}", null);
        }

        public override void OnDownloadFailure(DownloadFailedMessage message)
        {
            SendNotification(DOWNLOAD_FAILURE_TITLE, message.Message, null);
        }

        public override void OnManualInteractionRequired(ManualInteractionRequiredMessage message)
        {
            SendNotification(MANUAL_INTERACTION_REQUIRED_TITLE, message.Message, message.Artist, message.Album?.Albums);
        }

        public override void OnImportFailure(AlbumDownloadMessage message)
        {
            SendNotification(IMPORT_FAILURE_TITLE, message.Message, message.Artist);
        }

        public override void OnApplicationUpdate(ApplicationUpdateMessage updateMessage)
        {
            SendNotification(APPLICATION_UPDATE_TITLE, updateMessage.Message, null);
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            try
            {
                var isMarkdown = false;
                const string title = "Test Notification";

                var sb = new StringBuilder();
                sb.AppendLine("This is a test message from Lidarr");

                if (Settings.IncludeArtistPoster)
                {
                    isMarkdown = true;

                    sb.AppendLine("\r![](https://raw.githubusercontent.com/Lidarr/Lidarr/develop/Logo/128.png)");
                }

                var payload = new GotifyMessage
                {
                    Title = title,
                    Priority = Settings.Priority
                };

                if (Settings.MetadataLinks.Any())
                {
                    isMarkdown = true;

                    sb.AppendLine("");
                    sb.AppendLine("[Lidarr.audio](https://lidarr.audio)");
                    payload.SetClickUrl("https://lidarr.audio");
                }

                payload.Message = sb.ToString();

                payload.SetContentType(isMarkdown);

                _proxy.SendNotification(payload, Settings);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to send test message");
                failures.Add(new ValidationFailure("", "Unable to send test message"));
            }

            return new ValidationResult(failures);
        }

        private void SendNotification(string title, string message, Artist artist, List<Album> albums = null)
        {
            var poster = Settings.IncludeArtistPoster && artist != null
                ? artist.Metadata.Value.Images.FirstOrDefault(x => x.CoverType == MediaCoverTypes.Poster)?.RemoteUrl
                : null;
            var links = NotificationMetadataLinkGenerator.GenerateLinks(artist, albums, Settings.MetadataLinks);
            var isMarkdown = poster != null || links.Any();
            var sb = new StringBuilder();

            // Names are plain text, so markdown would render "M*A*S*H" in italics or a title shaped like a link as a link
            sb.AppendLine(isMarkdown ? EscapeMarkdown(message) : message);

            if (poster != null)
            {
                sb.AppendLine($"\r![]({poster})");
            }

            var payload = new GotifyMessage
            {
                Title = title,
                Priority = Settings.Priority
            };

            if (links.Any())
            {
                foreach (var link in links)
                {
                    // A blank line before each, or markdown runs the links together on one line
                    sb.AppendLine("");
                    sb.AppendLine($"[{link.Label}]({link.Link})");

                    if ((int?)link.Type == Settings.PreferredMetadataLink)
                    {
                        payload.SetClickUrl(link.Link);
                    }
                }
            }

            payload.Message = sb.ToString();
            payload.SetContentType(isMarkdown);

            _proxy.SendNotification(payload, Settings);
        }

        private static string EscapeMarkdown(string text)
        {
            return Regex.Replace(text, @"[\\`*_\[\]()#!<>]", @"\$0");
        }
    }
}
