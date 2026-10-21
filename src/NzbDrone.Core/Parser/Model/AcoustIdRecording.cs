using System.Collections.Generic;

namespace NzbDrone.Core.Parser.Model
{
    public class AcoustIdRecording
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public double? Duration { get; set; }
        public List<AcoustIdArtist> Artists { get; set; }
    }
}
