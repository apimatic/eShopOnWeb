using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class DeveloperSearchError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private DeveloperSearchError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static DeveloperSearchError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static DeveloperSearchError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<DeveloperSearchError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 429 or 500 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeveloperSearchErrorResponse : IErrorResponse<DeveloperSearchError>
{
    public static DeveloperSearchErrorResponse Instance { get; } = new();

    private DeveloperSearchErrorResponse()
    {
    }

    public Task<DeveloperSearchError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeveloperSearchError.Create(response, ct);
}
