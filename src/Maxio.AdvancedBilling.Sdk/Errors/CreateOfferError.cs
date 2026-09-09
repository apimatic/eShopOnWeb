using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CreateOfferError : ApiError
{
    private readonly Optional<ErrorArrayMapResponse1> _errorArrayMapResponse1Value;

    private CreateOfferError(Optional<ErrorArrayMapResponse1> errorArrayMapResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorArrayMapResponse1Value = errorArrayMapResponse1Value;
    }

    private static CreateOfferError AsErrorArrayMapResponse1(ErrorArrayMapResponse1 value) =>
        new(Optional<ErrorArrayMapResponse1>.Some(value), default);

    private static CreateOfferError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1 value) =>
        _errorArrayMapResponse1Value.TryGetValue(out value);

    internal static Task<CreateOfferError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorArrayMapResponse1>(response, ct).As(AsErrorArrayMapResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateOfferErrorResponse : IErrorResponse<CreateOfferError>
{
    public static CreateOfferErrorResponse Instance { get; } = new();

    private CreateOfferErrorResponse()
    {
    }

    public Task<CreateOfferError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateOfferError.Create(response, ct);
}
