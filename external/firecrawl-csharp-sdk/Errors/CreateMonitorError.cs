using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class CreateMonitorError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private CreateMonitorError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static CreateMonitorError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static CreateMonitorError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<CreateMonitorError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateMonitorErrorResponse : IErrorResponse<CreateMonitorError>
{
    public static CreateMonitorErrorResponse Instance { get; } = new();

    private CreateMonitorErrorResponse()
    {
    }

    public Task<CreateMonitorError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateMonitorError.Create(response, ct);
}
