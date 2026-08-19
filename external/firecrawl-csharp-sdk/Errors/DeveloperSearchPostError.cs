using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class DeveloperSearchPostError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private DeveloperSearchPostError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static DeveloperSearchPostError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static DeveloperSearchPostError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<DeveloperSearchPostError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 429 or 500 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeveloperSearchPostErrorResponse : IErrorResponse<DeveloperSearchPostError>
{
    public static DeveloperSearchPostErrorResponse Instance { get; } = new();

    private DeveloperSearchPostErrorResponse()
    {
    }

    public Task<DeveloperSearchPostError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeveloperSearchPostError.Create(response, ct);
}
