using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

public sealed class SingleAttemptPostHandler : DelegatingHandler
{
    private static readonly AsyncLocal<SendGate?> Gate = new();

    public static IDisposable BeginWriteScope() => new SendScope();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && Gate.Value is { } gate)
        {
            if (gate.Sent)
            {
                throw new DuplicateProviderWriteException();
            }

            gate.Sent = true;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class SendGate
    {
        public bool Sent { get; set; }
    }

    private sealed class SendScope : IDisposable
    {
        public SendScope() => Gate.Value = new SendGate();

        public void Dispose() => Gate.Value = null;
    }
}

internal sealed class DuplicateProviderWriteException : Exception
{
    public DuplicateProviderWriteException()
        : base("A duplicate provider write was refused.")
    {
    }
}
