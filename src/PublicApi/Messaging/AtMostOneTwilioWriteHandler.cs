using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

internal sealed class TwilioDuplicateWritePreventedException : Exception
{
    public TwilioDuplicateWritePreventedException()
        : base("A retried Twilio write was blocked because the original attempt may already have reached the provider.")
    {
    }
}

internal static class TwilioWriteGuard
{
    private static readonly AsyncLocal<int> Posts = new();

    public static IDisposable Begin()
    {
        Posts.Value = 0;
        return new Reset();
    }

    public static bool TryEnterPost()
    {
        var current = Posts.Value;
        if (current >= 1)
        {
            return false;
        }

        Posts.Value = current + 1;
        return true;
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => Posts.Value = 0;
    }
}

internal sealed class AtMostOneTwilioWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !TwilioWriteGuard.TryEnterPost())
        {
            throw new TwilioDuplicateWritePreventedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
