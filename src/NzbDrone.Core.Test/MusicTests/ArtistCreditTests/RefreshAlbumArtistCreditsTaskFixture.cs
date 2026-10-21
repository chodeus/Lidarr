using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music.ArtistCredits;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.ArtistCreditTests
{
    [TestFixture]
    public class RefreshAlbumArtistCreditsTaskFixture : CoreTest<TaskManager>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.GetMock<IScheduledTaskRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<ScheduledTask> { new ScheduledTask { Id = 1, TypeName = typeof(RefreshAlbumArtistCreditsCommand).FullName } });

            Subject.Handle(new ApplicationStartedEvent());
        }

        private void GivenExecuted(RefreshAlbumArtistCreditsCommand command)
        {
            Subject.Handle(new CommandExecutedEvent(new CommandModel { Body = command, StartedAt = DateTime.UtcNow }));
        }

        [Test]
        public void should_reset_the_timer_after_the_scheduled_run()
        {
            GivenExecuted(new RefreshAlbumArtistCreditsCommand());

            Mocker.GetMock<IScheduledTaskRepository>()
                  .Verify(r => r.SetLastExecutionTime(1, It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Once());
        }

        [Test]
        public void should_leave_the_timer_alone_after_a_run_for_just_added_albums()
        {
            GivenExecuted(new RefreshAlbumArtistCreditsCommand { AlbumIds = new List<int> { 5 } });

            Mocker.GetMock<IScheduledTaskRepository>()
                  .Verify(r => r.SetLastExecutionTime(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never());
        }
    }
}
