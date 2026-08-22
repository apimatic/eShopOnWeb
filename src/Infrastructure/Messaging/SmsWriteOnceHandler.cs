using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class SmsWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> Current = new();

    public static IDisposable BeginWrite() => new WriteScope();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && Current.Value is { } scope)
        {
            if (scope.Increment() > 1)
            {
                throw new DuplicateProviderWriteException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope : IDisposable
    {
        private int _sends;

        public WriteScope()
        {
            Current.Value = this;
        }

        public int Increment() => Interlocked.Increment(ref _sends);

        public void Dispose()
        {
            if (ReferenceEquals(Current.Value, this))
            {
                Current.Value = null;
            }
        }
    }
}

public sealed class DuplicateProviderWriteException : Exception
{
    public DuplicateProviderWriteException()
        : base("Refusing a retried provider write.")
    {
    }
}
