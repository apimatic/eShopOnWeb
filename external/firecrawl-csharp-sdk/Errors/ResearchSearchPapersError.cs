using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class ResearchSearchPapersError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ResearchSearchPapersError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ResearchSearchPapersError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ResearchSearchPapersError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ResearchSearchPapersError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 429 or 500 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ResearchSearchPapersErrorResponse : IErrorResponse<ResearchSearchPapersError>
{
    public static ResearchSearchPapersErrorResponse Instance { get; } = new();

    private ResearchSearchPapersErrorResponse()
    {
    }

    public Task<ResearchSearchPapersError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ResearchSearchPapersError.Create(response, ct);
}
