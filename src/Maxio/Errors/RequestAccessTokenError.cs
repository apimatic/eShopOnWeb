using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Models;
using Maxio.Models;

namespace Maxio.Errors;

public sealed class RequestAccessTokenError : ApiError
{
    private readonly Optional<MaxioGatewayOAuthError> _maxioGatewayOAuthErrorValue;

    private RequestAccessTokenError(Optional<MaxioGatewayOAuthError> maxioGatewayOAuthErrorValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _maxioGatewayOAuthErrorValue = maxioGatewayOAuthErrorValue;
    }

    private static RequestAccessTokenError AsMaxioGatewayOAuthError(MaxioGatewayOAuthError value) =>
        new(Optional<MaxioGatewayOAuthError>.Some(value), default);

    private static RequestAccessTokenError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetMaxioGatewayOAuthError(out MaxioGatewayOAuthError value) =>
        _maxioGatewayOAuthErrorValue.TryGetValue(out value);

    internal static Task<RequestAccessTokenError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 => FromJson<MaxioGatewayOAuthError>(response, ct).As(AsMaxioGatewayOAuthError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class RequestAccessTokenErrorResponse : IErrorResponse<RequestAccessTokenError>
{
    public static RequestAccessTokenErrorResponse Instance { get; } = new();

    private RequestAccessTokenErrorResponse()
    {
    }

    public Task<RequestAccessTokenError> Map(HttpResponseMessage response, CancellationToken ct) =>
        RequestAccessTokenError.Create(response, ct);
}
