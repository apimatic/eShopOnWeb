using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class SearchSupportDocsError : ApiError
{
    private readonly Optional<SupportProxyErrorResponse> _supportProxyErrorResponseValue;

    private SearchSupportDocsError(Optional<SupportProxyErrorResponse> supportProxyErrorResponseValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _supportProxyErrorResponseValue = supportProxyErrorResponseValue;
    }

    private static SearchSupportDocsError AsSupportProxyErrorResponse(SupportProxyErrorResponse value) =>
        new(Optional<SupportProxyErrorResponse>.Some(value), default);

    private static SearchSupportDocsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSupportProxyErrorResponse(out SupportProxyErrorResponse value) =>
        _supportProxyErrorResponseValue.TryGetValue(out value);

    internal static Task<SearchSupportDocsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 503 or 504 => FromJson<SupportProxyErrorResponse>(response, ct).As(AsSupportProxyErrorResponse),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class SearchSupportDocsErrorResponse : IErrorResponse<SearchSupportDocsError>
{
    public static SearchSupportDocsErrorResponse Instance { get; } = new();

    private SearchSupportDocsErrorResponse()
    {
    }

    public Task<SearchSupportDocsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        SearchSupportDocsError.Create(response, ct);
}
