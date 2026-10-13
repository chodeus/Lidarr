using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(909)]
    public class add_metadata_sources : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("MetadataSources")
                  .WithColumn("Enable").AsBoolean().NotNullable()
                  .WithColumn("Name").AsString().NotNullable()
                  .WithColumn("Implementation").AsString().NotNullable()
                  .WithColumn("Settings").AsString().Nullable()
                  .WithColumn("ConfigContract").AsString().Nullable();

            Create.TableForModel("MetadataSourceProfiles")
                  .WithColumn("Name").AsString().NotNullable()
                  .WithColumn("Tags").AsString().NotNullable()
                  .WithColumn("Order").AsInt32().NotNullable()
                  .WithColumn("PrimaryCombination").AsInt32().NotNullable()
                  .WithColumn("Items").AsString().NotNullable();

            // Lidarr's own server alone, so lookups behave as before until a profile is edited
            Insert.IntoTable("MetadataSources").Row(new
            {
                Enable = true,
                Name = "Lidarr",
                Implementation = "LidarrMetadataSource",
                Settings = "{}",
                ConfigContract = "LidarrMetadataSourceSettings"
            });

            Insert.IntoTable("MetadataSourceProfiles").Row(new
            {
                Name = "Default",
                Tags = "[]",
                Order = int.MaxValue,
                PrimaryCombination = 0,
                Items = "[{\"metadataSourceId\":1,\"role\":\"primary\"}]"
            });
        }
    }
}
