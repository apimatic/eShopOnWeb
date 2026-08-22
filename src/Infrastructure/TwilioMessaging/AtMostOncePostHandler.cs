using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.TwilioMessaging;

/// <summary>
/// Blocks SDK transport retries of POST so a write is attempted at most once.
/// State lives in AsyncLocal because each retry builds a new HttpRequestMessage.
/// </summary>
public sealed class AtMostOncePostHandler : DelegatingHandler
{
    private static readonly AsyncLocal<PostSendScope?> Scope = new();

    internal static IDisposable BeginPostScope() => new PostSendScope();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post)
        {
            var scope = Scope.Value;
            if (scope is not null)
            {
                if (scope.Attempted)
                {
                    throw new DuplicatePostRefusedException();
                }

                scope.Attempted = true;
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class PostSendScope : IDisposable
    {
        public bool Attempted { get; set; }

        public PostSendScope()
        {
            Scope.Value = this;
        }

        public void Dispose()
        {
            if (ReferenceEquals(Scope.Value, this))
            {
                Scope.Value = null;
            }
        }
    }
}

internal sealed class DuplicatePostRefusedException : Exception
{
    public DuplicatePostRefusedException()
        : base("A retry of this messaging write was refused because the first attempt may already have reached the provider.")
    {
    }
}
