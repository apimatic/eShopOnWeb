using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class GetMonitorCheckError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private GetMonitorCheckError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static GetMonitorCheckError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static GetMonitorCheckError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<GetMonitorCheckError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetMonitorCheckErrorResponse : IErrorResponse<GetMonitorCheckError>
{
    public static GetMonitorCheckErrorResponse Instance { get; } = new();

    private GetMonitorCheckErrorResponse()
    {
    }

    public Task<GetMonitorCheckError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetMonitorCheckError.Create(response, ct);
}
