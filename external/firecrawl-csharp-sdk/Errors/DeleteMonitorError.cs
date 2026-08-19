using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class DeleteMonitorError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private DeleteMonitorError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static DeleteMonitorError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static DeleteMonitorError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<DeleteMonitorError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeleteMonitorErrorResponse : IErrorResponse<DeleteMonitorError>
{
    public static DeleteMonitorErrorResponse Instance { get; } = new();

    private DeleteMonitorErrorResponse()
    {
    }

    public Task<DeleteMonitorError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeleteMonitorError.Create(response, ct);
}
