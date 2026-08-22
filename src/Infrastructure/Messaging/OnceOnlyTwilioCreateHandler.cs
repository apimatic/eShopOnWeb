using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class TwilioCreateWriteScope
{
    internal static readonly AsyncLocal<WriteState?> Current = new();

    internal sealed class WriteState
    {
        public int SendCount { get; set; }
    }

    public static IDisposable Begin()
    {
        Current.Value = new WriteState();
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}

internal sealed class OnceOnlyTwilioCreateHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsMessageCreate(request))
        {
            var state = TwilioCreateWriteScope.Current.Value;
            if (state != null)
            {
                if (state.SendCount >= 1)
                {
                    throw new DuplicateTwilioWriteException();
                }

                state.SendCount++;
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsMessageCreate(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri == null)
        {
            return false;
        }

        var path = request.RequestUri.AbsolutePath;
        return path.EndsWith("/Messages.json", StringComparison.OrdinalIgnoreCase);
    }
}
