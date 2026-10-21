using System;
using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;

namespace NzbDrone.Common.Test.CacheTests
{
    [TestFixture]
    public class CachedFixture
    {
        private Cached<string> _cachedString = new Cached<string>();
        private Worker _worker;

        [SetUp]
        public void SetUp()
        {
            _cachedString = new Cached<string>();
            _worker = new Worker();
        }

        [Test]
        public void should_not_keep_a_value_loaded_across_a_clear()
        {
            var loading = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var load = new Thread(() => _cachedString.Get("Test", () =>
            {
                loading.Set();
                release.Wait();
                return "before the clear";
            }));

            load.Start();
            loading.Wait();
            _cachedString.Clear();
            release.Set();
            load.Join();

            _cachedString.Find("Test").Should().BeNull();
            _cachedString.Get("Test", () => "after the clear").Should().Be("after the clear");
        }

        [Test]
        public void should_return_the_value_loaded_across_a_clear()
        {
            var loading = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            string loaded = null;
            var load = new Thread(() => loaded = _cachedString.Get("Test", () =>
            {
                loading.Set();
                release.Wait();
                return "before the clear";
            }));

            load.Start();
            loading.Wait();
            _cachedString.Clear();
            release.Set();
            load.Join();

            loaded.Should().Be("before the clear");
        }

        [Test]
        public void should_call_function_once()
        {
            _cachedString.Get("Test", _worker.GetString);
            _cachedString.Get("Test", _worker.GetString);

            _worker.HitCount.Should().Be(1);
        }

        [Test]
        public void multiple_calls_should_return_same_result()
        {
            var first = _cachedString.Get("Test", _worker.GetString);
            var second = _cachedString.Get("Test", _worker.GetString);

            first.Should().Be(second);
        }

        [Test]
        public void should_be_able_to_update_key()
        {
            _cachedString.Set("Key", "Old");
            _cachedString.Set("Key", "New");

            _cachedString.Find("Key").Should().Be("New");
        }

        [Test]
        public void should_be_able_to_remove_key()
        {
            _cachedString.Set("Key", "Value");

            _cachedString.Remove("Key");

            _cachedString.Find("Key").Should().BeNull();
        }

        [Test]
        public void should_be_able_to_remove_non_existing_key()
        {
            _cachedString.Remove("Key");
        }

        [Test]
        public void should_store_null()
        {
            var hitCount = 0;

            for (var i = 0; i < 10; i++)
            {
                _cachedString.Get("key", () =>
                    {
                        hitCount++;
                        return null;
                    });
            }

            hitCount.Should().Be(1);
        }

        [Test]
        [Platform(Exclude = "MacOsX")]
        public void should_honor_ttl()
        {
            var hitCount = 0;
            _cachedString = new Cached<string>();

            for (var i = 0; i < 10; i++)
            {
                _cachedString.Get("key",
                    () =>
                        {
                            hitCount++;
                            return null;
                        },
                    TimeSpan.FromMilliseconds(300));

                Thread.Sleep(100);
            }

            hitCount.Should().BeInRange(3, 7);
        }
    }

    public class Worker
    {
        public int HitCount { get; private set; }

        public string GetString()
        {
            HitCount++;
            return "Hit count is " + HitCount;
        }
    }
}
