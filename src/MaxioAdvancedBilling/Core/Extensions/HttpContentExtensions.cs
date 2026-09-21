using System.Net.Http;

namespace MaxioAdvancedBilling.Core.Extensions;

internal static class HttpContentExtension
{
    extension(HttpContent)
    {
        public static HttpContent None => null!;
    }
}
