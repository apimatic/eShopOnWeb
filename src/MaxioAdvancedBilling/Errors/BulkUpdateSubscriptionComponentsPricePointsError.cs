using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class BulkUpdateSubscriptionComponentsPricePointsError : ApiError
{
    private readonly Optional<ComponentPricePointError1> _componentPricePointError1Value;

    private BulkUpdateSubscriptionComponentsPricePointsError(Optional<ComponentPricePointError1> componentPricePointError1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _componentPricePointError1Value = componentPricePointError1Value;
    }

    private static BulkUpdateSubscriptionComponentsPricePointsError AsComponentPricePointError1(ComponentPricePointError1 value) =>
        new(Optional<ComponentPricePointError1>.Some(value), default);

    private static BulkUpdateSubscriptionComponentsPricePointsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetComponentPricePointError1(out ComponentPricePointError1 value) =>
        _componentPricePointError1Value.TryGetValue(out value);

    internal static Task<BulkUpdateSubscriptionComponentsPricePointsError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ComponentPricePointError1>(response, ct).As(AsComponentPricePointError1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class BulkUpdateSubscriptionComponentsPricePointsErrorResponse : IErrorResponse<BulkUpdateSubscriptionComponentsPricePointsError>
{
    public static BulkUpdateSubscriptionComponentsPricePointsErrorResponse Instance { get; } = new();

    private BulkUpdateSubscriptionComponentsPricePointsErrorResponse()
    {
    }

    public Task<BulkUpdateSubscriptionComponentsPricePointsError> Map(HttpResponseMessage response,
        CancellationToken ct) => BulkUpdateSubscriptionComponentsPricePointsError.Create(response, ct);
}
