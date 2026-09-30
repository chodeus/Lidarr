using System.Collections.Generic;
using FluentValidation.Results;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Notifications.Pushcut
{
    public class Pushcut : NotificationBase<PushcutSettings>
    {
        private readonly IPushcutProxy _proxy;

        public Pushcut(IPushcutProxy proxy)
        {
            _proxy = proxy;
        }

        public override string Name => "Pushcut";

        public override string Link => "https://www.pushcut.io";

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            failures.AddIfNotNull(_proxy.Test(Settings));

            return new ValidationResult(failures);
        }

        public override void OnGrab(GrabMessage grabMessage)
        {
            _proxy.SendNotification(ALBUM_GRABBED_TITLE, grabMessage.Message, GetLinks(grabMessage.Artist, grabMessage.RemoteAlbum?.Albums), Settings);
        }

        public override void OnReleaseImport(AlbumDownloadMessage message)
        {
            _proxy.SendNotification(ALBUM_DOWNLOADED_TITLE, message.Message, GetLinks(message.Artist, message.Album == null ? null : new List<Album> { message.Album }), Settings);
        }

        public override void OnArtistAdd(ArtistAddMessage message)
        {
            _proxy.SendNotification(ARTIST_ADDED_TITLE, $"{message.Artist.Name} added to library", GetLinks(message.Artist), Settings);
        }

        public override void OnArtistDelete(ArtistDeleteMessage deleteMessage)
        {
            _proxy.SendNotification(ARTIST_DELETED_TITLE, deleteMessage.Message, GetLinks(deleteMessage.Artist), Settings);
        }

        public override void OnAlbumDelete(AlbumDeleteMessage deleteMessage)
        {
            _proxy.SendNotification(ALBUM_DELETED_TITLE, deleteMessage.Message, GetLinks(deleteMessage.Album?.Artist?.Value, deleteMessage.Album == null ? null : new List<Album> { deleteMessage.Album }), Settings);
        }

        public override void OnHealthIssue(HealthCheck.HealthCheck healthCheck)
        {
            _proxy.SendNotification(HEALTH_ISSUE_TITLE, healthCheck.Message, new List<NotificationMetadataLink>(), Settings);
        }

        public override void OnHealthRestored(HealthCheck.HealthCheck previousCheck)
        {
            _proxy.SendNotification(HEALTH_RESTORED_TITLE, $"The following issue is now resolved: {previousCheck.Message}", new List<NotificationMetadataLink>(), Settings);
        }

        public override void OnDownloadFailure(DownloadFailedMessage message)
        {
            _proxy.SendNotification(DOWNLOAD_FAILURE_TITLE, message.Message, new List<NotificationMetadataLink>(), Settings);
        }

        public override void OnManualInteractionRequired(ManualInteractionRequiredMessage message)
        {
            _proxy.SendNotification(MANUAL_INTERACTION_REQUIRED_TITLE, message.Message, GetLinks(message.Artist, message.Album?.Albums), Settings);
        }

        public override void OnImportFailure(AlbumDownloadMessage message)
        {
            _proxy.SendNotification(IMPORT_FAILURE_TITLE, message.Message, new List<NotificationMetadataLink>(), Settings);
        }

        public override void OnApplicationUpdate(ApplicationUpdateMessage updateMessage)
        {
            _proxy.SendNotification(APPLICATION_UPDATE_TITLE, updateMessage.Message, new List<NotificationMetadataLink>(), Settings);
        }

        private List<NotificationMetadataLink> GetLinks(Artist artist, List<Album> albums = null)
        {
            return NotificationMetadataLinkGenerator.GenerateLinks(artist, albums, Settings.MetadataLinks);
        }
    }
}
