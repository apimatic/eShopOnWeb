using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class TwilioOnceWriteScope : IDisposable
{
    private static readonly AsyncLocal<WriteState?> Current = new();

    public TwilioOnceWriteScope()
    {
        Current.Value = new WriteState();
    }

    public static bool IsArmed => Current.Value is not null;

    public static bool TryBeginWrite()
    {
        var state = Current.Value;
        if (state is null)
        {
            return true;
        }

        if (state.WriteStarted)
        {
            return false;
        }

        state.WriteStarted = true;
        return true;
    }

    public void Dispose()
    {
        Current.Value = null;
    }

    private sealed class WriteState
    {
        public bool WriteStarted;
    }
}

internal sealed class TwilioDuplicateWriteException : Exception
{
    public TwilioDuplicateWriteException()
        : base("A retried provider write was blocked because the original attempt may already have been accepted.")
    {
    }
}

public sealed class TwilioOnceWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (TwilioOnceWriteScope.IsArmed && request.Method == HttpMethod.Post)
        {
            if (!TwilioOnceWriteScope.TryBeginWrite())
            {
                throw new TwilioDuplicateWriteException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
