using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class RunMonitorError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private RunMonitorError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static RunMonitorError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static RunMonitorError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<RunMonitorError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            409 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class RunMonitorErrorResponse : IErrorResponse<RunMonitorError>
{
    public static RunMonitorErrorResponse Instance { get; } = new();

    private RunMonitorErrorResponse()
    {
    }

    public Task<RunMonitorError> Map(HttpResponseMessage response, CancellationToken ct) =>
        RunMonitorError.Create(response, ct);
}
