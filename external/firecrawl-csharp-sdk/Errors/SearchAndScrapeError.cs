using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class SearchAndScrapeError : ApiError
{
    private readonly Optional<Search408Error1> _search408Error1Value;

    private readonly Optional<Search500Error1> _search500Error1Value;

    private SearchAndScrapeError(Optional<Search408Error1> search408Error1Value,
        Optional<Search500Error1> search500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _search408Error1Value = search408Error1Value;
        _search500Error1Value = search500Error1Value;
    }

    private static SearchAndScrapeError AsSearch408Error1(Search408Error1 value) =>
        new(Optional<Search408Error1>.Some(value), default, default);

    private static SearchAndScrapeError AsSearch500Error1(Search500Error1 value) =>
        new(default, Optional<Search500Error1>.Some(value), default);

    private static SearchAndScrapeError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetSearch408Error1(out Search408Error1 value) =>
        _search408Error1Value.TryGetValue(out value);

    public bool TryGetSearch500Error1(out Search500Error1 value) =>
        _search500Error1Value.TryGetValue(out value);

    internal static Task<SearchAndScrapeError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            408 => FromJson<Search408Error1>(response, ct).As(AsSearch408Error1),
            500 => FromJson<Search500Error1>(response, ct).As(AsSearch500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class SearchAndScrapeErrorResponse : IErrorResponse<SearchAndScrapeError>
{
    public static SearchAndScrapeErrorResponse Instance { get; } = new();

    private SearchAndScrapeErrorResponse()
    {
    }

    public Task<SearchAndScrapeError> Map(HttpResponseMessage response, CancellationToken ct) =>
        SearchAndScrapeError.Create(response, ct);
}
