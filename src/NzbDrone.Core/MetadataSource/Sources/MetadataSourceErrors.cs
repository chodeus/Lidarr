using System;
using System.Net;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.SkyHook;

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
                    case UnexpectedHtmlContentException html:
                        return $"HTTP {(int)html.Response.StatusCode}, browser content instead of API data";
                    case HttpException http:
                        return $"HTTP {(int)http.Response.StatusCode}";
                    case WebException web:
                        return $"request failed ({web.Status})";
                }
            }

            // SkyHookProxy builds the errors it throws without an inner exception from fixed text
            return ex is SkyHookException && ex.InnerException == null ? ex.Message : ex.GetBaseException().GetType().Name;
        }
    }
}
