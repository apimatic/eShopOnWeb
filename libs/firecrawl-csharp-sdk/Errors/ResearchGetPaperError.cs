using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class ResearchGetPaperError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ResearchGetPaperError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ResearchGetPaperError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ResearchGetPaperError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ResearchGetPaperError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 404 or 429 or 500 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ResearchGetPaperErrorResponse : IErrorResponse<ResearchGetPaperError>
{
    public static ResearchGetPaperErrorResponse Instance { get; } = new();

    private ResearchGetPaperErrorResponse()
    {
    }

    public Task<ResearchGetPaperError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ResearchGetPaperError.Create(response, ct);
}
