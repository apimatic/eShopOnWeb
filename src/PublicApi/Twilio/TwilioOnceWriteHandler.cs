using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

/// <summary>
/// Holds a send-once flag in the caller's async context so a transport retry cannot
/// deliver a second POST to the messaging provider.
/// </summary>
internal sealed class TwilioOnceWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> Current = new();

    public static IDisposable BeginWrite() => new WriteScope();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && Current.Value is { } scope)
        {
            if (scope.Sent)
            {
                throw new TwilioDuplicateWriteException();
            }

            scope.Sent = true;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope : IDisposable
    {
        public bool Sent { get; set; }

        public WriteScope()
        {
            Current.Value = this;
        }

        public void Dispose()
        {
            if (ReferenceEquals(Current.Value, this))
            {
                Current.Value = null;
            }
        }
    }
}
