using System;
using System.Linq;
using FluentAssertions;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.MusicTests
{
    public static class RefreshedFields
    {
        // Every stored column of the refreshed copy, other than those others own, must have been saved
        public static void ShouldMatch<T>(T refreshed, T stored, params string[] ownedByOthers)
        {
            var notStored = TableMapping.Mapper.ExcludeProperties(typeof(T)).Select(p => p.Name).ToList();
            var columns = typeof(T).GetProperties()
                                   .Where(p => p.CanRead && p.CanWrite && !ownedByOthers.Contains(p.Name) && !notStored.Contains(p.Name))
                                   .Where(p => !p.PropertyType.IsGenericType || !p.PropertyType.GetGenericTypeDefinition().Name.StartsWith("Lazy"));

            foreach (var column in columns)
            {
                var expected = column.GetValue(refreshed);
                var actual = column.GetValue(stored);

                if (expected is DateTime expectedDate && actual is DateTime actualDate)
                {
                    actualDate.Should().BeCloseTo(expectedDate, TimeSpan.FromSeconds(1), column.Name);
                }
                else
                {
                    actual.Should().BeEquivalentTo(expected, column.Name);
                }
            }
        }
    }
}
