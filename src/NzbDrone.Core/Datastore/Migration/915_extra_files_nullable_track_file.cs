using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // A release's own file, such as its .cue or rip log, has no track file; MetadataFiles already allows that
    [Migration(915)]
    public class extra_files_nullable_track_file : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ExtraFiles").AlterColumn("TrackFileId").AsInt32().Nullable();
        }
    }
}
