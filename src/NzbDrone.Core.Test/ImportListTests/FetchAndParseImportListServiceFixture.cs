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

        private Mock<IImportList> WithList(int id, ImportListFetchResult fetchResult, int? syncDeletedCount = null)
        {
            var importListDefinition = new ImportListDefinition { Id = id, EnableAutomaticAdd = true };

            var mockImportList = new Mock<IImportList>();
            mockImportList.SetupGet(s => s.Definition).Returns(importListDefinition);
            mockImportList.Setup(s => s.Fetch()).Returns(fetchResult);
            mockImportList.SetupGet(s => s.MinRefreshInterval).Returns(TimeSpan.FromHours(12));

            Mocker.GetMock<IImportListStatusService>()
                .Setup(v => v.GetListStatus(id))
                .Returns(new ImportListStatus());

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
            WithList(1, new ImportListFetchResult { Items = _listItems, AnyFailure = false });

            var listResult = Subject.Fetch();
            listResult.AnyFailure.Should().BeFalse();
            listResult.Items.Should().HaveCount(5);

            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(1, false), Times.Once());
            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncItemsForList(_listItems, 1), Times.Once());
        }

        [Test]
        public void should_only_store_items_for_lists_that_dont_fail()
        {
            WithList(1, new ImportListFetchResult { Items = _listItems, AnyFailure = false });
            WithList(2, new ImportListFetchResult { Items = _listItems, AnyFailure = true });

            var listResult = Subject.Fetch();
            listResult.AnyFailure.Should().BeTrue();

            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncItemsForList(_listItems, 1), Times.Once());
            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(2, It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncItemsForList(It.IsAny<List<ImportListItemInfo>>(), 2), Times.Never());
        }

        [Test]
        public void should_set_removed_flag_if_list_has_removed_items()
        {
            WithList(1, new ImportListFetchResult { Items = _listItems, AnyFailure = false }, syncDeletedCount: 500);

            Subject.Fetch();

            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(1, true), Times.Once());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_only_store_single_list_items_if_it_doesnt_fail(bool anyFailure)
        {
            var mockImportList = WithList(1, new ImportListFetchResult { Items = _listItems, AnyFailure = anyFailure });

            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.GetInstance(It.IsAny<ImportListDefinition>()))
                .Returns(mockImportList.Object);

            var listResult = Subject.FetchSingleList((ImportListDefinition)mockImportList.Object.Definition);
            listResult.AnyFailure.Should().Be(anyFailure);

            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncItemsForList(_listItems, 1), anyFailure ? Times.Never() : Times.Once());
        }
    }
}
