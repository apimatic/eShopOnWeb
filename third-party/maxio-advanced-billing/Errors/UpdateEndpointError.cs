using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class UpdateEndpointError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private UpdateEndpointError(Optional<RawError> noContentValue,
        Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static UpdateEndpointError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default, default);

    private static UpdateEndpointError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(default, Optional<ErrorListResponse1>.Some(value), default);

    private static UpdateEndpointError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<UpdateEndpointError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdateEndpointErrorResponse : IErrorResponse<UpdateEndpointError>
{
    public static UpdateEndpointErrorResponse Instance { get; } = new();

    private UpdateEndpointErrorResponse()
    {
    }

    public Task<UpdateEndpointError> Map(HttpResponseMessage response, CancellationToken ct) =>
        UpdateEndpointError.Create(response, ct);
}
