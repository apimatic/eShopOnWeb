using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class UpdateMonitorError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private UpdateMonitorError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static UpdateMonitorError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static UpdateMonitorError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<UpdateMonitorError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdateMonitorErrorResponse : IErrorResponse<UpdateMonitorError>
{
    public static UpdateMonitorErrorResponse Instance { get; } = new();

    private UpdateMonitorErrorResponse()
    {
    }

    public Task<UpdateMonitorError> Map(HttpResponseMessage response, CancellationToken ct) =>
        UpdateMonitorError.Create(response, ct);
}
