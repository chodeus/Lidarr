using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Profiles.Tagging;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles.Tagging
{
    // Real service over the real test database; the mock repository only forwards, or injects a second writer mid-call
    [TestFixture]
    public class TaggingProfileServiceDbFixture : DbTest<TaggingProfileService, TaggingProfile>
    {
        private TaggingProfileRepository _realRepo;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            _realRepo = Mocker.Resolve<TaggingProfileRepository>();
            Mocker.SetConstant<ITaggingProfileRepository>(_realRepo);
        }

        private TaggingProfile InsertProfile(string name, int order, params int[] tags)
        {
            return Db.Insert(new TaggingProfile { Name = name, Order = order, Tags = new HashSet<int>(tags), WriteAudioTags = WriteAudioTagsType.No });
        }

        private Mock<ITaggingProfileRepository> Forwarding(Action<IList<TaggingProfile>> beforeManyWrite = null)
        {
            var mock = new Mock<ITaggingProfileRepository>();
            mock.Setup(r => r.All()).Returns(() => _realRepo.All());
            mock.Setup(r => r.Get(It.IsAny<int>())).Returns<int>(id => _realRepo.Get(id));
            mock.Setup(r => r.Insert(It.IsAny<TaggingProfile>())).Returns<TaggingProfile>(p => _realRepo.Insert(p));
            mock.Setup(r => r.Update(It.IsAny<TaggingProfile>())).Returns<TaggingProfile>(p => _realRepo.Update(p));
            mock.Setup(r => r.Delete(It.IsAny<int>())).Callback<int>(id => _realRepo.Delete(id));
            mock.Setup(r => r.UpdateMany(It.IsAny<IList<TaggingProfile>>()))
                .Callback<IList<TaggingProfile>>(m =>
                {
                    beforeManyWrite?.Invoke(m);
                    _realRepo.UpdateMany(m);
                });
            mock.Setup(r => r.SetFields(It.IsAny<IList<TaggingProfile>>(), It.IsAny<Expression<Func<TaggingProfile, object>>[]>()))
                .Callback<IList<TaggingProfile>, Expression<Func<TaggingProfile, object>>[]>((m, p) =>
                {
                    beforeManyWrite?.Invoke(m);
                    _realRepo.SetFields(m, p);
                });
            return mock;
        }

        private void SaveSeededDefault(WriteAudioTagsType writeAudioTags)
        {
            var seeded = _realRepo.Get(1);
            seeded.WriteAudioTags = writeAudioTags;
            new TaggingProfileService(_realRepo, new CacheManager()).Update(seeded);
        }

        [Test]
        public void seeded_default_should_be_id_1_and_last()
        {
            _realRepo.All().Should().ContainSingle(p => p.Id == 1).Which.Order.Should().Be(int.MaxValue);
        }

        [Test]
        public void add_should_rank_below_existing_profiles_when_one_holds_the_maximum_order()
        {
            var existing = InsertProfile("Existing", 1, 5);
            var legacy = InsertProfile("Moved after the default by an older reorder", int.MaxValue, 6);

            var added = Subject.Add(new TaggingProfile { Name = "Added", Tags = new HashSet<int> { 5 } });

            var stored = _realRepo.All().Where(p => p.Id != 1).ToList();
            stored.Single(p => p.Id == existing.Id).Order.Should().Be(1);
            stored.Single(p => p.Id == legacy.Id).Order.Should().Be(2);
            stored.Single(p => p.Id == added.Id).Order.Should().Be(3);
            Subject.BestForTags(new HashSet<int> { 5 }).Id.Should().Be(existing.Id);
        }

        [Test]
        public void add_should_not_rewrite_existing_orders_when_there_is_room()
        {
            InsertProfile("A", 1, 5);
            InsertProfile("B", 7, 6);
            var repo = Forwarding();

            var added = new TaggingProfileService(repo.Object, new CacheManager()).Add(new TaggingProfile { Name = "C", Tags = new HashSet<int> { 7 } });

            added.Order.Should().Be(8);
            repo.Verify(r => r.SetFields(It.IsAny<IList<TaggingProfile>>(), It.IsAny<Expression<Func<TaggingProfile, object>>[]>()), Times.Never());
            repo.Verify(r => r.UpdateMany(It.IsAny<IList<TaggingProfile>>()), Times.Never());
        }

        [Test]
        public void reorder_should_not_revert_a_default_profile_save_that_lands_mid_write()
        {
            var a = InsertProfile("A", 1, 5);
            InsertProfile("B", 2, 6);

            // Settings > Metadata saves the seeded default while the reorder is writing
            var repo = Forwarding(_ => SaveSeededDefault(WriteAudioTagsType.AllFiles));

            new TaggingProfileService(repo.Object, new CacheManager()).Reorder(a.Id, 1);

            _realRepo.Get(1).WriteAudioTags.Should().Be(WriteAudioTagsType.AllFiles);
        }

        [Test]
        public void reorder_then_default_save_in_sequence_should_keep_both()
        {
            var a = InsertProfile("A", 1, 5);
            var b = InsertProfile("B", 2, 6);

            Subject.Reorder(a.Id, 1);
            SaveSeededDefault(WriteAudioTagsType.AllFiles);

            _realRepo.Get(1).WriteAudioTags.Should().Be(WriteAudioTagsType.AllFiles);
            _realRepo.Get(a.Id).Order.Should().BeGreaterThan(_realRepo.Get(b.Id).Order);
        }

        [Test]
        public void update_should_not_revert_a_reorder_from_another_request()
        {
            InsertProfile("A", 1, 5);
            InsertProfile("B", 2, 6);
            var c = InsertProfile("C", 3, 7);

            var inWindow = new ManualResetEventSlim();
            var reorderDone = new ManualResetEventSlim();
            var repo = Forwarding();
            repo.Setup(r => r.Update(It.IsAny<TaggingProfile>()))
                .Returns<TaggingProfile>(p =>
                {
                    inWindow.Set();

                    // Unserialised, the other request's reorder lands here; serialised, it waits for this update
                    reorderDone.Wait(TimeSpan.FromMilliseconds(500));
                    return _realRepo.Update(p);
                });

            var subject = new TaggingProfileService(repo.Object, new CacheManager());
            var edit = _realRepo.Get(c.Id);
            edit.Name = "C renamed";

            var update = Task.Run(() => subject.Update(edit));
            inWindow.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            var reorder = Task.Run(() =>
            {
                subject.Reorder(c.Id, null);
                reorderDone.Set();
            });

            Task.WaitAll(new[] { update, reorder }, TimeSpan.FromSeconds(10)).Should().BeTrue();

            var stored = _realRepo.All().Where(p => p.Id != 1).ToList();
            stored.Single(p => p.Id == c.Id).Order.Should().Be(1);
            stored.Single(p => p.Id == c.Id).Name.Should().Be("C renamed");
            stored.Select(p => p.Order).Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void update_then_reorder_in_sequence_should_keep_both()
        {
            InsertProfile("A", 1, 5);
            InsertProfile("B", 2, 6);
            var c = InsertProfile("C", 3, 7);

            var edit = _realRepo.Get(c.Id);
            edit.Name = "C renamed";
            Subject.Update(edit);
            Subject.Reorder(c.Id, null);

            var stored = _realRepo.All().Where(p => p.Id != 1).ToList();
            stored.Single(p => p.Id == c.Id).Order.Should().Be(1);
            stored.Single(p => p.Id == c.Id).Name.Should().Be("C renamed");
            stored.Select(p => p.Order).Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void delete_should_not_leave_the_deleted_profile_cached_when_the_renumber_fails()
        {
            var a = InsertProfile("A", 1, 5);
            InsertProfile("B", 2, 6);

            var repo = Forwarding(_ => throw new InvalidOperationException("database is locked"));
            var subject = new TaggingProfileService(repo.Object, new CacheManager());

            subject.BestForTags(new HashSet<int> { 5 }).Id.Should().Be(a.Id);

            Assert.Throws<InvalidOperationException>(() => subject.Delete(a.Id));

            _realRepo.Find(a.Id).Should().BeNull();
            subject.BestForTags(new HashSet<int> { 5 }).Id.Should().NotBe(a.Id);
        }

        [Test]
        public void reorder_and_delete_should_persist_dense_orders_with_the_default_last()
        {
            var a = InsertProfile("A", 4, 5);
            var b = InsertProfile("B", 9, 6);
            var c = InsertProfile("C", 9, 7);

            Subject.Reorder(c.Id, 1);

            _realRepo.All().OrderBy(p => p.Order).Select(p => p.Id).Should().Equal(a.Id, b.Id, c.Id, 1);

            Subject.Delete(a.Id);

            var stored = _realRepo.All().ToList();
            stored.Single(p => p.Id == b.Id).Order.Should().Be(1);
            stored.Single(p => p.Id == c.Id).Order.Should().Be(2);
            stored.Single(p => p.Id == 1).Order.Should().Be(int.MaxValue);
        }

        [Test]
        public void update_should_keep_the_stored_order_and_pin_the_default()
        {
            var a = InsertProfile("A", 2, 5);

            Subject.Update(new TaggingProfile { Id = a.Id, Name = "A", Order = 0, Tags = new HashSet<int> { 5 } });
            Subject.Update(new TaggingProfile { Id = 1, Name = "Default", Order = 0, Tags = new HashSet<int>() });

            _realRepo.Get(a.Id).Order.Should().Be(2);
            _realRepo.Get(1).Order.Should().Be(int.MaxValue);
        }
    }
}
