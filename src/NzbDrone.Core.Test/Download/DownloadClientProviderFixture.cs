using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadClientProviderFixture : CoreTest<DownloadClientProvider>
    {
        private List<IDownloadClient> _downloadClients;
        private List<DownloadClientStatus> _blockedProviders;
        private int _nextId;

        [SetUp]
        public void SetUp()
        {
            _downloadClients = new List<IDownloadClient>();
            _blockedProviders = new List<DownloadClientStatus>();
            _nextId = 1;

            Mocker.GetMock<IDownloadClientFactory>()
                  .Setup(v => v.GetAvailableProviders())
                  .Returns(_downloadClients);

            Mocker.GetMock<IDownloadClientStatusService>()
                  .Setup(v => v.GetBlockedProviders())
                  .Returns(_blockedProviders);
        }

        private Mock<IDownloadClient> WithUsenetClient(int priority = 0, HashSet<int> tags = null)
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition)
                .Returns(Builder<DownloadClientDefinition>
                    .CreateNew()
                    .With(v => v.Id = _nextId++)
                    .With(v => v.Priority = priority)
                    .With(v => v.Tags = tags ?? new HashSet<int>())
                    .Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(nameof(UsenetDownloadProtocol));

            return mock;
        }

        private Mock<IDownloadClient> WithTorrentClient(int priority = 0, HashSet<int> tags = null)
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition)
                .Returns(Builder<DownloadClientDefinition>
                    .CreateNew()
                    .With(v => v.Id = _nextId++)
                    .With(v => v.Priority = priority)
                    .With(v => v.Tags = tags ?? new HashSet<int>())
                    .Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(nameof(TorrentDownloadProtocol));

            return mock;
        }

        private void WithTorrentIndexer(int downloadClientId)
        {
            Mocker.GetMock<IIndexerFactory>()
                .Setup(v => v.Find(It.IsAny<int>()))
                .Returns(Builder<IndexerDefinition>
                    .CreateNew()
                    .With(v => v.Id = _nextId++)
                    .With(v => v.DownloadClientId = downloadClientId)
                    .Build());
        }

        private void GivenBlockedClient(int id)
        {
            _blockedProviders.Add(new DownloadClientStatus
            {
                ProviderId = id,
                DisabledTill = DateTime.UtcNow.AddHours(3)
            });
        }

        [Test]
        public void should_roundrobin_over_usenet_client()
        {
            WithUsenetClient();
            WithUsenetClient();
            WithUsenetClient();
            WithTorrentClient();

            var client1 = Subject.GetDownloadClient(nameof(UsenetDownloadProtocol));
            var client2 = Subject.GetDownloadClient(nameof(UsenetDownloadProtocol));
            var client3 = Subject.GetDownloadClient(nameof(UsenetDownloadProtocol));
            var client4 = Subject.GetDownloadClient(nameof(UsenetDownloadProtocol));
            var client5 = Subject.GetDownloadClient(nameof(UsenetDownloadProtocol));

            client1.Definition.Id.Should().Be(1);
            client2.Definition.Id.Should().Be(2);
            client3.Definition.Id.Should().Be(3);
            client4.Definition.Id.Should().Be(1);
            client5.Definition.Id.Should().Be(2);
        }

        [Test]
        public void should_roundrobin_over_torrent_client()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();

            var client1 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client2 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client3 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client4 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client5 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));

            client1.Definition.Id.Should().Be(2);
            client2.Definition.Id.Should().Be(3);
            client3.Definition.Id.Should().Be(4);
            client4.Definition.Id.Should().Be(2);
            client5.Definition.Id.Should().Be(3);
        }

        [Test]
        public void should_roundrobin_over_protocol_separately()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();

            var client1 = Subject.GetDownloadClient(nameof(UsenetDownloadProtocol));
            var client2 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client3 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client4 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));

            client1.Definition.Id.Should().Be(1);
            client2.Definition.Id.Should().Be(2);
            client3.Definition.Id.Should().Be(3);
            client4.Definition.Id.Should().Be(2);
        }

        [Test]
        public void should_roundrobin_over_clients_with_matching_tags()
        {
            var seriesTags = new HashSet<int> { 1 };
            var clientTags = new HashSet<int> { 1 };

            WithTorrentClient();
            WithTorrentClient(0, clientTags);
            WithTorrentClient();
            WithTorrentClient(0, clientTags);

            var client1 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 0, false, seriesTags);
            var client2 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 0, false, seriesTags);
            var client3 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 0, false, seriesTags);
            var client4 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 0, false, seriesTags);

            client1.Definition.Id.Should().Be(2);
            client2.Definition.Id.Should().Be(4);
            client3.Definition.Id.Should().Be(2);
            client4.Definition.Id.Should().Be(4);
        }

        [Test]
        public void should_roundrobin_over_non_tagged_when_no_matching_tags()
        {
            var seriesTags = new HashSet<int> { 2 };
            var clientTags = new HashSet<int> { 1 };

            WithTorrentClient();
            WithTorrentClient(0, clientTags);
            WithTorrentClient();
            WithTorrentClient(0, clientTags);

            var client1 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 0, false, seriesTags);
            var client2 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 0, false, seriesTags);
            var client3 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 0, false, seriesTags);
            var client4 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 0, false, seriesTags);

            client1.Definition.Id.Should().Be(1);
            client2.Definition.Id.Should().Be(3);
            client3.Definition.Id.Should().Be(1);
            client4.Definition.Id.Should().Be(3);
        }

        [Test]
        public void should_fail_to_choose_when_clients_have_tags_but_no_match()
        {
            var seriesTags = new HashSet<int> { 2 };
            var clientTags = new HashSet<int> { 1 };

            WithTorrentClient(0, clientTags);

            Assert.Throws<DownloadClientUnavailableException>(() => Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 0, false, seriesTags));
        }

        [Test]
        public void should_skip_blocked_torrent_client()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();

            GivenBlockedClient(3);

            var client1 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client2 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client3 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client4 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));

            client1.Definition.Id.Should().Be(2);
            client2.Definition.Id.Should().Be(4);
            client3.Definition.Id.Should().Be(2);
            client4.Definition.Id.Should().Be(4);
        }

        [Test]
        public void should_not_skip_blocked_torrent_client_if_all_blocked()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();

            GivenBlockedClient(2);
            GivenBlockedClient(3);
            GivenBlockedClient(4);

            var client1 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client2 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client3 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client4 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client5 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));

            client1.Definition.Id.Should().Be(2);
            client2.Definition.Id.Should().Be(3);
            client3.Definition.Id.Should().Be(4);
            client4.Definition.Id.Should().Be(2);
        }

        [Test]
        public void should_skip_secondary_prio_torrent_client()
        {
            WithUsenetClient();
            WithTorrentClient(2);
            WithTorrentClient();
            WithTorrentClient();

            var client1 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client2 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client3 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client4 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client5 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));

            client1.Definition.Id.Should().Be(3);
            client2.Definition.Id.Should().Be(4);
            client3.Definition.Id.Should().Be(3);
            client4.Definition.Id.Should().Be(4);
        }

        [Test]
        public void should_not_skip_secondary_prio_torrent_client_if_primary_blocked()
        {
            WithUsenetClient();
            WithTorrentClient(2);
            WithTorrentClient(2);
            WithTorrentClient();

            GivenBlockedClient(4);

            var client1 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client2 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client3 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client4 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));
            var client5 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol));

            client1.Definition.Id.Should().Be(2);
            client2.Definition.Id.Should().Be(3);
            client3.Definition.Id.Should().Be(2);
            client4.Definition.Id.Should().Be(3);
        }

        [Test]
        public void should_always_choose_indexer_client()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentIndexer(3);

            var client1 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 1);
            var client2 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 1);
            var client3 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 1);
            var client4 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 1);
            var client5 = Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 1);

            client1.Definition.Id.Should().Be(3);
            client2.Definition.Id.Should().Be(3);
            client3.Definition.Id.Should().Be(3);
            client4.Definition.Id.Should().Be(3);
            client5.Definition.Id.Should().Be(3);
        }

        [Test]
        public void should_fail_to_choose_client_when_indexer_reference_does_not_exist()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentIndexer(5);

            Assert.Throws<DownloadClientUnavailableException>(() => Subject.GetDownloadClient(nameof(TorrentDownloadProtocol), 1));
        }

        [Test]
        public void should_return_all_available_clients_for_protocol()
        {
            WithUsenetClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();

            var clients = Subject.GetDownloadClients(nameof(TorrentDownloadProtocol));

            clients.Select(c => c.Definition.Id).Should().BeEquivalentTo(new[] { 2, 3, 4 });
        }

        [Test]
        public void should_return_no_clients_when_none_has_the_protocol()
        {
            WithUsenetClient();
            WithUsenetClient();

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol)).Should().BeEmpty();
        }

        [Test]
        public void should_order_clients_by_priority_then_by_id()
        {
            WithTorrentClient(priority: 1);
            WithTorrentClient(priority: 0);
            WithTorrentClient(priority: 0);
            WithTorrentClient(priority: 2);

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol)).Select(c => c.Definition.Id).Should().Equal(2, 3, 1, 4);
        }

        [Test]
        public void should_start_after_the_last_client_that_took_a_release()
        {
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol)).First().Definition.Id.Should().Be(1);

            Subject.ReportSuccessfulDownloadClient(nameof(TorrentDownloadProtocol), 2);

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol)).Select(c => c.Definition.Id).Should().Equal(3, 1, 2);
        }

        [Test]
        public void should_start_after_the_last_client_that_took_a_release_when_it_is_no_longer_listed()
        {
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();

            Subject.ReportSuccessfulDownloadClient(nameof(TorrentDownloadProtocol), 2);
            GivenBlockedClient(2);

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol), filterBlockedClients: true).Select(c => c.Definition.Id).Should().Equal(3, 1);
        }

        [Test]
        public void should_keep_the_last_used_client_of_each_protocol_apart()
        {
            WithTorrentClient();
            WithTorrentClient();

            Subject.ReportSuccessfulDownloadClient(nameof(UsenetDownloadProtocol), 1);

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol)).Select(c => c.Definition.Id).Should().Equal(1, 2);
        }

        [Test]
        public void should_list_only_clients_with_a_matching_tag()
        {
            var artistTags = new HashSet<int> { 1, 2 };

            WithTorrentClient(tags: new HashSet<int> { 1 });
            WithTorrentClient(tags: new HashSet<int> { 3 });
            WithTorrentClient(tags: new HashSet<int> { 2 });
            WithTorrentClient();

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol), tags: artistTags).Select(c => c.Definition.Id).Should().BeEquivalentTo(new[] { 1, 3 });
        }

        [Test]
        public void should_list_untagged_clients_when_no_tag_matches()
        {
            var artistTags = new HashSet<int> { 5 };

            WithTorrentClient(tags: new HashSet<int> { 1 });
            WithTorrentClient(tags: new HashSet<int> { 2 });
            WithTorrentClient();
            WithTorrentClient();

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol), tags: artistTags).Select(c => c.Definition.Id).Should().BeEquivalentTo(new[] { 3, 4 });
        }

        [Test]
        public void should_refuse_a_list_when_every_client_has_another_tag()
        {
            var artistTags = new HashSet<int> { 5 };

            WithTorrentClient(tags: new HashSet<int> { 1 });
            WithTorrentClient(tags: new HashSet<int> { 2 });

            Assert.Throws<DownloadClientUnavailableException>(() => Subject.GetDownloadClients(nameof(TorrentDownloadProtocol), tags: artistTags));
        }

        [Test]
        public void should_list_only_the_indexers_own_client()
        {
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentIndexer(2);

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol), indexerId: 1).Select(c => c.Definition.Id).Should().Equal(2);
        }

        [Test]
        public void should_refuse_a_list_when_the_indexers_client_does_not_exist()
        {
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentIndexer(5);

            Assert.Throws<DownloadClientUnavailableException>(() => Subject.GetDownloadClients(nameof(TorrentDownloadProtocol), indexerId: 1));
        }

        [Test]
        public void should_leave_out_blocked_clients()
        {
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentClient();

            GivenBlockedClient(2);

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol), filterBlockedClients: true).Select(c => c.Definition.Id).Should().Equal(1, 3);
        }

        [Test]
        public void should_refuse_a_list_when_every_client_is_blocked_and_blocked_clients_are_filtered()
        {
            WithTorrentClient();
            WithTorrentClient();

            GivenBlockedClient(1);
            GivenBlockedClient(2);

            Assert.Throws<DownloadClientUnavailableException>(() => Subject.GetDownloadClients(nameof(TorrentDownloadProtocol), filterBlockedClients: true));
        }

        [Test]
        public void should_list_blocked_clients_when_every_client_is_blocked()
        {
            WithTorrentClient();
            WithTorrentClient();

            GivenBlockedClient(1);
            GivenBlockedClient(2);

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol)).Select(c => c.Definition.Id).Should().Equal(1, 2);
        }

        [Test]
        public void should_refuse_a_list_when_the_indexers_client_is_blocked_and_blocked_clients_are_filtered()
        {
            WithTorrentClient();
            WithTorrentClient();
            WithTorrentIndexer(2);

            GivenBlockedClient(2);

            Assert.Throws<DownloadClientUnavailableException>(() => Subject.GetDownloadClients(nameof(TorrentDownloadProtocol), indexerId: 1, filterBlockedClients: true));
        }

        [Test]
        public void should_order_tagged_clients_by_priority()
        {
            var artistTags = new HashSet<int> { 1 };

            WithTorrentClient(priority: 1, tags: new HashSet<int> { 1 });
            WithTorrentClient(priority: 0, tags: new HashSet<int> { 1 });
            WithTorrentClient(priority: 0, tags: new HashSet<int> { 2 });
            WithTorrentClient(priority: 2);

            Subject.GetDownloadClients(nameof(TorrentDownloadProtocol), tags: artistTags).Select(c => c.Definition.Id).Should().Equal(2, 1);
        }
    }
}
