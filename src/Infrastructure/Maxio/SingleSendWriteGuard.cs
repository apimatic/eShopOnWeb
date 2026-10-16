using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class SingleSendBlockedException : Exception
{
    public SingleSendBlockedException() : base("A billing write was already sent.")
    {
    }
}

public static class MaxioWriteScope
{
    private static readonly AsyncLocal<Scope?> Current = new();

    public static IDisposable Begin()
    {
        var prior = Current.Value;
        Current.Value = new Scope();
        return new Pop(prior);
    }

    internal static void CountOutgoingWrite(HttpRequestMessage request)
    {
        var scope = Current.Value;
        if (scope is null || !IsGuardedWrite(request))
            return;
        if (scope.Sends >= 1)
            throw new SingleSendBlockedException();
        scope.Sends++;
    }

    private static bool IsGuardedWrite(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post)
            return false;
        var path = request.RequestUri?.AbsolutePath;
        if (string.IsNullOrEmpty(path))
            return false;
        return path.EndsWith("/customers.json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/subscriptions.json", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Scope
    {
        public int Sends;
    }

    private sealed class Pop : IDisposable
    {
        private readonly Scope? _prior;

        public Pop(Scope? prior) => _prior = prior;

        public void Dispose() => Current.Value = _prior;
    }
}

public sealed class SingleSendWriteGuard : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        MaxioWriteScope.CountOutgoingWrite(request);
        return base.SendAsync(request, cancellationToken);
    }
}
