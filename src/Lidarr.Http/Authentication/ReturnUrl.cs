using System;
using NzbDrone.Common.Extensions;

namespace Lidarr.Http.Authentication
{
    public static class ReturnUrl
    {
        // Only a local URL, kept inside the URL base; anything else goes home
        public static string Resolve(string urlBase, string returnUrl, Func<string, bool> isLocalUrl)
        {
            if (returnUrl.IsNullOrWhiteSpace() || !isLocalUrl(returnUrl))
            {
                return urlBase + "/";
            }

            if (urlBase.IsNullOrWhiteSpace() ||
                returnUrl.Equals(urlBase, StringComparison.OrdinalIgnoreCase) ||
                returnUrl.StartsWith(urlBase + "/", StringComparison.OrdinalIgnoreCase))
            {
                return returnUrl;
            }

            return urlBase + returnUrl;
        }
    }
}
