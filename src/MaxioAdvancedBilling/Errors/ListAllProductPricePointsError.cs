using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListAllProductPricePointsError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private ListAllProductPricePointsError(Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static ListAllProductPricePointsError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    private static ListAllProductPricePointsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<ListAllProductPricePointsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListAllProductPricePointsErrorResponse : IErrorResponse<ListAllProductPricePointsError>
{
    public static ListAllProductPricePointsErrorResponse Instance { get; } = new();

    private ListAllProductPricePointsErrorResponse()
    {
    }

    public Task<ListAllProductPricePointsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListAllProductPricePointsError.Create(response, ct);
}
