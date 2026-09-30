using System.Collections.Generic;
using FluentValidation.Results;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Notifications.Telegram
{
    public class Telegram : NotificationBase<TelegramSettings>
    {
        private readonly ITelegramProxy _proxy;

        public Telegram(ITelegramProxy proxy)
        {
            _proxy = proxy;
        }

        public override string Name => "Telegram";
        public override string Link => "https://telegram.org/";

        public override void OnGrab(GrabMessage grabMessage)
        {
            var title = Settings.IncludeAppNameInTitle ? ALBUM_GRABBED_TITLE_BRANDED : ALBUM_GRABBED_TITLE;

            _proxy.SendNotification(title, grabMessage.Message, GetLinks(grabMessage.Artist, grabMessage.RemoteAlbum?.Albums), Settings);
        }

        public override void OnReleaseImport(AlbumDownloadMessage message)
        {
            var title = Settings.IncludeAppNameInTitle ? ALBUM_DOWNLOADED_TITLE_BRANDED : ALBUM_DOWNLOADED_TITLE;

            _proxy.SendNotification(title, message.Message, GetLinks(message.Artist, message.Album == null ? null : new List<Album> { message.Album }), Settings);
        }

        public override void OnArtistAdd(ArtistAddMessage message)
        {
            var title = Settings.IncludeAppNameInTitle ? ARTIST_ADDED_TITLE_BRANDED : ARTIST_ADDED_TITLE;

            _proxy.SendNotification(title, message.Message, GetLinks(message.Artist), Settings);
        }

        public override void OnArtistDelete(ArtistDeleteMessage deleteMessage)
        {
            var title = Settings.IncludeAppNameInTitle ? ARTIST_DELETED_TITLE_BRANDED : ARTIST_DELETED_TITLE;

            _proxy.SendNotification(title, deleteMessage.Message, GetLinks(deleteMessage.Artist), Settings);
        }

        public override void OnAlbumDelete(AlbumDeleteMessage deleteMessage)
        {
            var title = Settings.IncludeAppNameInTitle ? ALBUM_DELETED_TITLE_BRANDED : ALBUM_DELETED_TITLE;

            _proxy.SendNotification(title, deleteMessage.Message, GetLinks(deleteMessage.Album?.Artist?.Value, deleteMessage.Album == null ? null : new List<Album> { deleteMessage.Album }), Settings);
        }

        public override void OnHealthIssue(HealthCheck.HealthCheck healthCheck)
        {
            var title = Settings.IncludeAppNameInTitle ? HEALTH_ISSUE_TITLE_BRANDED : HEALTH_ISSUE_TITLE;

            _proxy.SendNotification(title, healthCheck.Message, new List<NotificationMetadataLink>(), Settings);
        }

        public override void OnHealthRestored(HealthCheck.HealthCheck previousCheck)
        {
            var title = Settings.IncludeAppNameInTitle ? HEALTH_RESTORED_TITLE_BRANDED : HEALTH_RESTORED_TITLE;

            _proxy.SendNotification(title, $"The following issue is now resolved: {previousCheck.Message}", new List<NotificationMetadataLink>(), Settings);
        }

        public override void OnDownloadFailure(DownloadFailedMessage message)
        {
            var title = Settings.IncludeAppNameInTitle ? DOWNLOAD_FAILURE_TITLE_BRANDED : DOWNLOAD_FAILURE_TITLE;

            _proxy.SendNotification(title, message.Message, new List<NotificationMetadataLink>(), Settings);
        }

        public override void OnManualInteractionRequired(ManualInteractionRequiredMessage message)
        {
            var title = Settings.IncludeAppNameInTitle ? MANUAL_INTERACTION_REQUIRED_TITLE_BRANDED : MANUAL_INTERACTION_REQUIRED_TITLE;

            _proxy.SendNotification(title, message.Message, GetLinks(message.Artist, message.Album?.Albums), Settings);
        }

        public override void OnImportFailure(AlbumDownloadMessage message)
        {
            var title = Settings.IncludeAppNameInTitle ? IMPORT_FAILURE_TITLE_BRANDED : IMPORT_FAILURE_TITLE;

            _proxy.SendNotification(title, message.Message, GetLinks(message.Artist), Settings);
        }

        public override void OnApplicationUpdate(ApplicationUpdateMessage updateMessage)
        {
            var title = Settings.IncludeAppNameInTitle ? APPLICATION_UPDATE_TITLE_BRANDED : APPLICATION_UPDATE_TITLE;

            _proxy.SendNotification(title, updateMessage.Message, new List<NotificationMetadataLink>(), Settings);
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            failures.AddIfNotNull(_proxy.Test(Settings));

            return new ValidationResult(failures);
        }

        private List<NotificationMetadataLink> GetLinks(Artist artist, List<Album> albums = null)
        {
            return NotificationMetadataLinkGenerator.GenerateLinks(artist, albums, Settings.MetadataLinks);
        }
    }
}
