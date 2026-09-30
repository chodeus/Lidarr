using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    public class ImportListStatusServiceFixture : CoreTest<ImportListStatusService>
    {
        private DateTime _epoch;

        [SetUp]
        public void SetUp()
        {
            _epoch = DateTime.UtcNow;

            Mocker.GetMock<IRuntimeInfo>()
                .SetupGet(v => v.StartTime)
                .Returns(_epoch - TimeSpan.FromHours(1));
        }

        private void WithStatus(ImportListStatus status)
        {
            Mocker.GetMock<IImportListStatusRepository>()
                .Setup(v => v.FindByProviderId(1))
                .Returns(status);

            Mocker.GetMock<IImportListStatusRepository>()
                .Setup(v => v.All())
                .Returns(new[] { status });
        }

        private void VerifyUpdate()
        {
            Mocker.GetMock<IImportListStatusRepository>()
                .Verify(v => v.Upsert(It.IsAny<ImportListStatus>()), Times.Once());
        }

        private void VerifyNoUpdate()
        {
            Mocker.GetMock<IImportListStatusRepository>()
                  .Verify(v => v.Upsert(It.IsAny<ImportListStatus>()), Times.Never());
        }

        [Test]
        public void should_cancel_backoff_on_success()
        {
            WithStatus(new ImportListStatus { EscalationLevel = 2 });

            Subject.RecordSuccess(1);

            VerifyUpdate();

            var status = Subject.GetBlockedProviders().FirstOrDefault();
            status.Should().BeNull();
        }

        [Test]
        public void should_not_store_update_if_already_okay()
        {
            WithStatus(new ImportListStatus { EscalationLevel = 0 });

            Subject.RecordSuccess(1);

            VerifyNoUpdate();
        }

        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        public void should_keep_removed_items_flag_until_cleaned(bool pending, bool removedItems, bool expected)
        {
            var status = new ImportListStatus { HasRemovedItemSinceLastClean = pending };
            WithStatus(status);

            Subject.UpdateListSyncStatus(1, removedItems);

            status.HasRemovedItemSinceLastClean.Should().Be(expected);
            status.LastInfoSync.Should().NotBeNull();
            VerifyUpdate();
        }

        [Test]
        public void should_not_set_removed_items_flag_from_develop_overload()
        {
            var status = new ImportListStatus();
            WithStatus(status);

            Subject.UpdateListSyncStatus(1);

            status.HasRemovedItemSinceLastClean.Should().BeFalse();
            status.LastInfoSync.Should().NotBeNull();
        }

        [Test]
        public void should_clear_removed_items_flag_when_marked_as_cleaned()
        {
            var status = new ImportListStatus { HasRemovedItemSinceLastClean = true };
            WithStatus(status);

            Subject.MarkListsAsCleaned();

            status.HasRemovedItemSinceLastClean.Should().BeFalse();
            Mocker.GetMock<IImportListStatusRepository>()
                  .Verify(v => v.SetFields(It.Is<IList<ImportListStatus>>(l => l.Count == 1), It.IsAny<System.Linq.Expressions.Expression<Func<ImportListStatus, object>>[]>()), Times.Once());
        }
    }
}
