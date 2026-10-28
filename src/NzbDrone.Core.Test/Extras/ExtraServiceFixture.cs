using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras
{
    [TestFixture]
    public class ExtraServiceFixture : CoreTest
    {
        [Test]
        public void should_relink_every_kind_of_extra_file_when_one_fails()
        {
            var failing = new Mock<IRelinkExtraFiles>();
            failing.Setup(r => r.Relink(It.IsAny<int>(), It.IsAny<TrackFile>())).Throws(new InvalidOperationException("database is locked"));
            var working = new Mock<IRelinkExtraFiles>();
            var subject = new ExtraService(Mocker.Resolve<IMediaFileService>(), Mocker.Resolve<IAlbumService>(), Mocker.Resolve<ITrackService>(), Mocker.Resolve<IDiskProvider>(), Mocker.Resolve<IConfigService>(), new List<IManageExtraFiles>(), new List<IRelinkExtraFiles> { failing.Object, working.Object }, TestLogger);

            subject.RelinkTrackFile(3, new TrackFile { Id = 4 });

            working.Verify(r => r.Relink(3, It.Is<TrackFile>(f => f.Id == 4)), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
