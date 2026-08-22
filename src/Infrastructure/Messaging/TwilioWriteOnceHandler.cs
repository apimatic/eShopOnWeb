using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Refuses a transport-layer retry of a POST so a duplicate SMS cannot reach the provider.
/// Scope is opened around a single SDK write; GET/lookup calls do not use it.
/// </summary>
public sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> Scope = new();

    public static WriteScope Begin()
    {
        var scope = new WriteScope();
        Scope.Value = scope;
        return scope;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post)
        {
            var scope = Scope.Value;
            if (scope is not null && !scope.TryBeginPost())
            {
                throw new DuplicateTwilioWriteException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    public sealed class WriteScope : IDisposable
    {
        private int _posts;

        public bool TryBeginPost() => Interlocked.Increment(ref _posts) == 1;

        public void Dispose()
        {
            if (ReferenceEquals(Scope.Value, this))
            {
                Scope.Value = null;
            }
        }
    }
}

public sealed class DuplicateTwilioWriteException : Exception
{
    public DuplicateTwilioWriteException()
        : base("A duplicate Twilio write was refused.")
    {
    }
}
