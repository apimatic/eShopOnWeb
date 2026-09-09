using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CreateEndpointError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private CreateEndpointError(Optional<ErrorListResponse1> errorListResponse1Value, Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static CreateEndpointError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    private static CreateEndpointError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<CreateEndpointError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateEndpointErrorResponse : IErrorResponse<CreateEndpointError>
{
    public static CreateEndpointErrorResponse Instance { get; } = new();

    private CreateEndpointErrorResponse()
    {
    }

    public Task<CreateEndpointError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateEndpointError.Create(response, ct);
}
