using System.Net.Http;

namespace PayPalServerSdk.Core.Request;

internal sealed class EmptyBody : IRequest
{
    public static EmptyBody Instance { get; } = new();

    private EmptyBody() { }

    // No content at all — a zero-length StringContent would make .NET Framework's
    // HttpClientHandler throw ProtocolViolationException on GET ("Cannot send a
    // content-body with this verb-type").
    public HttpContent Get() => null!;

    public bool CanRetry => true;
}