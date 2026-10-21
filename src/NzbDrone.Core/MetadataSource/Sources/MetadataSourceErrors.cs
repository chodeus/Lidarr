using System;
using System.Net;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource.Sources
{
    public static class MetadataSourceErrors
    {
        // A failed request's message, and SkyHook's wrapper around it, hold the URL, and a custom server's URL can carry a token in the path
        public static string Describe(Exception ex)
        {
            for (var inner = ex; inner != null; inner = inner.InnerException)
            {
                switch (inner)
                {
                    case HttpException http:
                        return $"HTTP {(int)http.Response.StatusCode}";
                    case WebException web:
                        return $"request failed ({web.Status})";
                }
            }

            return ex.Message;
        }
    }
}
