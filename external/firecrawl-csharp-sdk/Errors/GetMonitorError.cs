using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class GetMonitorError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private GetMonitorError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static GetMonitorError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static GetMonitorError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<GetMonitorError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetMonitorErrorResponse : IErrorResponse<GetMonitorError>
{
    public static GetMonitorErrorResponse Instance { get; } = new();

    private GetMonitorErrorResponse()
    {
    }

    public Task<GetMonitorError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetMonitorError.Create(response, ct);
}
