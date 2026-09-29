using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.ArtistCredits;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests.ArtistCreditTests
{
    [TestFixture]
    public class AlbumArtistCreditServiceFixture : CoreTest<AlbumArtistCreditService>
    {
        private List<AlbumArtistCreditCandidate> _candidates;

        private static List<AlbumArtistCreditName> Credit(params string[] names)
        {
            return names.Select((n, i) => new AlbumArtistCreditName { Name = n, ForeignArtistId = i == 0 ? "primary" : "guest" + i, JoinPhrase = i < names.Length - 1 ? " & " : "" }).ToList();
        }

        private void GivenCandidates(int count)
        {
            _candidates = Enumerable.Range(1, count)
                .Select(i => new AlbumArtistCreditCandidate { AlbumId = i, ForeignAlbumId = "rg" + i, ForeignArtistId = "primary" })
                .ToList();

            Mocker.GetMock<IAlbumArtistCreditRepository>()
                  .Setup(r => r.GetCandidates(It.IsAny<DateTime>(), It.IsAny<int>()))
                  .Returns(() => _candidates);

            Mocker.GetMock<IAlbumArtistCreditRepository>()
                  .Setup(r => r.FindByAlbumIds(It.IsAny<List<int>>()))
                  .Returns(new List<AlbumArtistCredit>());
        }

        private void GivenCredits(params List<AlbumArtistCreditName>[] credits)
        {
            var queue = new Queue<List<AlbumArtistCreditName>>(credits);

            Mocker.GetMock<IMusicBrainzArtistCreditProxy>()
                  .Setup(p => p.GetCredit(It.IsAny<string>()))
                  .Returns(() => queue.Dequeue());
        }

        [TearDown]
        public void TearDown()
        {
            AlbumArtistCreditLookup.Reset();
        }

        [Test]
        public void should_store_the_credit_of_each_candidate()
        {
            GivenCandidates(2);
            GivenCredits(Credit("Artist Name", "Guest One"), Credit("Artist Name"));

            Subject.Execute(new RefreshAlbumArtistCreditsCommand());

            Mocker.GetMock<IAlbumArtistCreditRepository>()
                  .Verify(r => r.Upsert(It.Is<AlbumArtistCredit>(c => c.AlbumId == 1 && c.Guests == "Guest One")), Times.Once());
            Mocker.GetMock<IAlbumArtistCreditRepository>()
                  .Verify(r => r.Upsert(It.Is<AlbumArtistCredit>(c => c.AlbumId == 2 && c.Guests == "")), Times.Once());
        }

        [Test]
        public void should_not_store_a_credit_it_could_not_read()
        {
            GivenCandidates(2);
            GivenCredits(null, Credit("Artist Name"));

            Subject.Execute(new RefreshAlbumArtistCreditsCommand());

            Mocker.GetMock<IAlbumArtistCreditRepository>()
                  .Verify(r => r.Upsert(It.Is<AlbumArtistCredit>(c => c.AlbumId == 1)), Times.Never());
            Mocker.GetMock<IAlbumArtistCreditRepository>()
                  .Verify(r => r.Upsert(It.Is<AlbumArtistCredit>(c => c.AlbumId == 2)), Times.Once());
        }

        [Test]
        public void should_store_an_empty_credit_for_an_unknown_release_group()
        {
            GivenCandidates(1);
            GivenCredits(new List<AlbumArtistCreditName>());

            Subject.Execute(new RefreshAlbumArtistCreditsCommand());

            Mocker.GetMock<IAlbumArtistCreditRepository>()
                  .Verify(r => r.Upsert(It.Is<AlbumArtistCredit>(c => c.AlbumId == 1 && c.Credit == "")), Times.Once());
        }

        [Test]
        public void should_stop_when_musicbrainz_keeps_failing()
        {
            GivenCandidates(10);
            GivenCredits(Enumerable.Repeat<List<AlbumArtistCreditName>>(null, 10).ToArray());

            Subject.Execute(new RefreshAlbumArtistCreditsCommand());

            Mocker.GetMock<IMusicBrainzArtistCreditProxy>()
                  .Verify(p => p.GetCredit(It.IsAny<string>()), Times.Exactly(AlbumArtistCreditService.MaxConsecutiveFailures));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_stop_when_failures_are_not_consecutive()
        {
            GivenCandidates(9);
            GivenCredits(null, null, null, null, Credit("Artist Name"), null, null, null, null);

            Subject.Execute(new RefreshAlbumArtistCreditsCommand());

            Mocker.GetMock<IMusicBrainzArtistCreditProxy>()
                  .Verify(p => p.GetCredit(It.IsAny<string>()), Times.Exactly(9));
        }

        [Test]
        public void should_update_an_existing_credit_in_place()
        {
            GivenCandidates(1);
            GivenCredits(Credit("Artist Name", "Guest One"));
            Mocker.GetMock<IAlbumArtistCreditRepository>()
                  .Setup(r => r.FindByAlbumIds(It.IsAny<List<int>>()))
                  .Returns(new List<AlbumArtistCredit> { new AlbumArtistCredit { Id = 7, AlbumId = 1 } });

            Subject.Execute(new RefreshAlbumArtistCreditsCommand());

            Mocker.GetMock<IAlbumArtistCreditRepository>()
                  .Verify(r => r.Upsert(It.Is<AlbumArtistCredit>(c => c.Id == 7 && c.AlbumId == 1)), Times.Once());
        }

        [Test]
        public void should_stop_when_lidarr_is_shutting_down()
        {
            GivenCandidates(3);
            GivenCredits(Credit("Artist Name"), Credit("Artist Name"), Credit("Artist Name"));

            Subject.Handle(new ApplicationShutdownRequested());
            Subject.Execute(new RefreshAlbumArtistCreditsCommand());

            Mocker.GetMock<IMusicBrainzArtistCreditProxy>().Verify(p => p.GetCredit(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_the_credit_of_a_deleted_album()
        {
            Subject.Handle(new AlbumDeletedEvent(new Album { Id = 3 }, false, false));

            Mocker.GetMock<IAlbumArtistCreditRepository>().Verify(r => r.DeleteByAlbumId(3), Times.Once());
        }

        [Test]
        public void should_serve_the_lookup_once_started()
        {
            Mocker.GetMock<IAlbumArtistCreditRepository>()
                  .Setup(r => r.FindByAlbumId(4))
                  .Returns(new AlbumArtistCredit { AlbumId = 4, Credit = "Artist Name & Guest One", Guests = "Guest One" });
            Mocker.GetMock<IConfigService>().SetupGet(c => c.WriteAlbumArtistCredits).Returns(true);

            Subject.Handle(new ApplicationStartedEvent());

            AlbumArtistCreditLookup.Guests(new Album { Id = 4 }).Should().Be("Guest One");
            AlbumArtistCreditLookup.AlbumArtistTag(new Album { Id = 4 }, "Artist Name").Should().Be("Artist Name & Guest One");
        }
    }
}
