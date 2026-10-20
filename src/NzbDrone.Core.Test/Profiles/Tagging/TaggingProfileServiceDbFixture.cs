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

        private static TaggingProfileService NewService(ITaggingProfileRepository repo)
        {
            return new TaggingProfileService(repo, new CacheManager());
        }

        private Mock<ITaggingProfileRepository> Forwarding(Action<IList<TaggingProfile>> beforeManyWrite = null, Action afterFirstRead = null)
        {
            var mock = new Mock<ITaggingProfileRepository>();
            var read = false;
            mock.Setup(r => r.All()).Returns(() =>
            {
                var snapshot = _realRepo.All().ToList();

                if (afterFirstRead != null && !read)
                {
                    read = true;
                    afterFirstRead();
                }

                return snapshot;
            });
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
        public void add_should_clear_the_cache_when_the_insert_fails_after_a_renumber()
        {
            var legacy = InsertProfile("Moved after the default by an older reorder", int.MaxValue, 5);

            var repo = Forwarding();
            repo.Setup(r => r.Insert(It.IsAny<TaggingProfile>())).Throws(new InvalidOperationException("database is locked"));
            var subject = NewService(repo.Object);

            subject.BestForTags(new HashSet<int> { 5 }).Order.Should().Be(int.MaxValue);

            Assert.Throws<InvalidOperationException>(() => subject.Add(new TaggingProfile { Name = "Added", Tags = new HashSet<int> { 6 } }));

            _realRepo.Get(legacy.Id).Order.Should().Be(1);
            subject.BestForTags(new HashSet<int> { 5 }).Order.Should().Be(1);
        }

        [Test]
        public void add_should_put_a_default_stored_out_of_place_back_last()
        {
            var seeded = _realRepo.Get(1);
            seeded.Order = 0;
            _realRepo.SetFields(seeded, p => p.Order);

            var added = Subject.Add(new TaggingProfile { Name = "Added", Tags = new HashSet<int> { 5 } });

            _realRepo.Get(1).Order.Should().Be(int.MaxValue);
            Subject.BestForTags(new HashSet<int> { 5 }).Id.Should().Be(added.Id);
        }

        [Test]
        public void best_for_tags_should_not_cache_the_ranking_a_concurrent_reorder_replaced()
        {
            var a = InsertProfile("A", 1, 5);
            var b = InsertProfile("B", 2, 5);

            TaggingProfileService subject = null;
            Task reorder = null;
            var repo = Forwarding(afterFirstRead: () =>
            {
                // Unserialised, the reorder commits and clears the cache here, before the old read is stored
                reorder = Task.Run(() => subject.Reorder(b.Id, null));
                reorder.Wait(TimeSpan.FromMilliseconds(500));
            });
            subject = NewService(repo.Object);

            subject.BestForTags(new HashSet<int> { 5 }).Id.Should().Be(a.Id);
            reorder.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            subject.BestForTags(new HashSet<int> { 5 }).Id.Should().Be(b.Id);
        }

        [Test]
        public void best_for_tags_should_not_cache_a_profile_a_concurrent_delete_removed()
        {
            var a = InsertProfile("A", 1, 5);
            InsertProfile("B", 2, 5);

            TaggingProfileService subject = null;
            Task delete = null;
            var repo = Forwarding(afterFirstRead: () =>
            {
                delete = Task.Run(() => subject.Delete(a.Id));
                delete.Wait(TimeSpan.FromMilliseconds(500));
            });
            subject = NewService(repo.Object);

            subject.BestForTags(new HashSet<int> { 5 }).Id.Should().Be(a.Id);
            delete.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            _realRepo.Find(a.Id).Should().BeNull();
            subject.BestForTags(new HashSet<int> { 5 }).Id.Should().NotBe(a.Id);
        }

        [Test]
        public void cached_lookup_should_not_wait_for_another_lookups_database_read()
        {
            InsertProfile("A", 1, 5);
            var b = InsertProfile("B", 2, 6);

            var inRead = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var hold = false;
            var repo = Forwarding();
            repo.Setup(r => r.All()).Returns(() =>
            {
                var snapshot = _realRepo.All().ToList();

                if (hold)
                {
                    inRead.Set();
                    release.Wait(TimeSpan.FromSeconds(5));
                }

                return snapshot;
            });
            var subject = NewService(repo.Object);

            subject.BestForTags(new HashSet<int> { 6 }).Id.Should().Be(b.Id);

            hold = true;
            var miss = Task.Run(() => subject.BestForTags(new HashSet<int> { 5 }));
            inRead.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            var hit = Task.Run(() => subject.BestForTags(new HashSet<int> { 6 }));
            var finished = hit.Wait(TimeSpan.FromMilliseconds(500));
            release.Set();
            miss.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            finished.Should().BeTrue();
            hit.Result.Id.Should().Be(b.Id);
        }

        [Test]
        public void cached_lookup_should_not_wait_for_a_writer()
        {
            InsertProfile("A", 1, 5);
            var b = InsertProfile("B", 2, 6);

            var inWrite = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var subject = NewService(Forwarding(_ =>
            {
                inWrite.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }).Object);

            subject.BestForTags(new HashSet<int> { 6 }).Id.Should().Be(b.Id);

            var reorder = Task.Run(() => subject.Reorder(b.Id, null));
            inWrite.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            var hit = Task.Run(() => subject.BestForTags(new HashSet<int> { 6 }));
            var finished = hit.Wait(TimeSpan.FromMilliseconds(500));
            release.Set();
            reorder.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            finished.Should().BeTrue();
        }

        [Test]
        public void seeded_default_should_not_cache_the_profile_a_concurrent_save_replaced()
        {
            TaggingProfileService subject = null;
            Task save = null;
            var repo = Forwarding(afterFirstRead: () =>
            {
                save = Task.Run(() =>
                {
                    var seeded = _realRepo.Get(1);
                    seeded.WriteAudioTags = WriteAudioTagsType.Sync;
                    subject.Update(seeded);
                });
                save.Wait(TimeSpan.FromMilliseconds(500));
            });
            subject = new TaggingProfileService(repo.Object, new CacheManager());

            subject.GetSeededDefaultProfile().WriteAudioTags.Should().NotBe(WriteAudioTagsType.Sync);
            save.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            subject.GetSeededDefaultProfile().WriteAudioTags.Should().Be(WriteAudioTagsType.Sync);
        }

        [Test]
        public void best_for_tags_should_not_pick_a_default_stored_out_of_place()
        {
            var seeded = _realRepo.Get(1);
            seeded.Order = 0;
            _realRepo.SetFields(seeded, p => p.Order);

            var tagged = InsertProfile("Tagged", 1, 5);

            Subject.BestForTags(new HashSet<int> { 5 }).Id.Should().Be(tagged.Id);
        }

        [Test]
        public void best_for_tags_should_not_pick_the_default_over_a_profile_left_at_the_maximum_order()
        {
            var legacy = InsertProfile("Moved after the default by an older reorder", int.MaxValue, 5);

            Subject.BestForTags(new HashSet<int> { 5 }).Id.Should().Be(legacy.Id);
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
