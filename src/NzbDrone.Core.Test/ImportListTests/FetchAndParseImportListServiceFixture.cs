using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class FetchAndParseImportListServiceFixture : CoreTest<FetchAndParseImportListService>
    {
        private List<IImportList> _importLists;
        private List<ImportListItemInfo> _listItems;

        [SetUp]
        public void Setup()
        {
            _importLists = new List<IImportList>();

            Mocker.GetMock<IImportListFactory>()
                  .Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>()))
                  .Returns(_importLists);

            _listItems = Builder<ImportListItemInfo>.CreateListOfSize(5)
                .Build().ToList();
        }

        private Mock<IImportList> WithList(int id, bool fails = false, int? syncDeletedCount = null)
        {
            var importListDefinition = new ImportListDefinition { Id = id, EnableAutomaticAdd = true };
            var status = new ImportListStatus { MostRecentFailure = DateTime.UtcNow.AddDays(-1) };

            var mockImportList = new Mock<IImportList>();
            mockImportList.SetupGet(s => s.Definition).Returns(importListDefinition);
            mockImportList.SetupGet(s => s.MinRefreshInterval).Returns(TimeSpan.FromHours(12));
            mockImportList.Setup(s => s.Fetch())
                          .Callback(() =>
                          {
                              // Lists record their own failure and return what they have
                              if (fails)
                              {
                                  status.MostRecentFailure = DateTime.UtcNow;
                              }
                          })
                          .Returns(_listItems);

            Mocker.GetMock<IImportListStatusService>()
                .Setup(v => v.GetListStatus(id))
                .Returns(status);

            if (syncDeletedCount.HasValue)
            {
                Mocker.GetMock<IImportListItemService>()
                    .Setup(v => v.SyncItemsForList(It.IsAny<List<ImportListItemInfo>>(), id))
                    .Returns(syncDeletedCount.Value);
            }

            _importLists.Add(mockImportList.Object);

            return mockImportList;
        }

        [Test]
        public void should_store_items_if_list_doesnt_fail()
        {
            WithList(1);

            Subject.Fetch().Should().HaveCount(5);

            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(1, false), Times.Once());
            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncItemsForList(It.Is<List<ImportListItemInfo>>(l => l.SequenceEqual(_listItems)), 1), Times.Once());
        }

        [Test]
        public void should_only_store_items_for_lists_that_dont_fail()
        {
            WithList(1);
            WithList(2, fails: true);

            Subject.Fetch();

            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncItemsForList(It.IsAny<List<ImportListItemInfo>>(), 1), Times.Once());
            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncItemsForList(It.IsAny<List<ImportListItemInfo>>(), 2), Times.Never());
            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(2, false), Times.Once());
        }

        [Test]
        public void should_set_removed_flag_if_list_has_removed_items()
        {
            WithList(1, syncDeletedCount: 500);

            Subject.Fetch();

            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(1, true), Times.Once());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_only_store_single_list_items_if_it_doesnt_fail(bool fails)
        {
            var mockImportList = WithList(1, fails);

            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.GetInstance(It.IsAny<ImportListDefinition>()))
                .Returns(mockImportList.Object);

            Subject.FetchSingleList((ImportListDefinition)mockImportList.Object.Definition);

            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncItemsForList(It.IsAny<List<ImportListItemInfo>>(), 1), fails ? Times.Never() : Times.Once());
        }
    }
}
