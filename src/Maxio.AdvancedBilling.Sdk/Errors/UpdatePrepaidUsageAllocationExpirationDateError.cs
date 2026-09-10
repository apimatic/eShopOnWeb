using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class UpdatePrepaidUsageAllocationExpirationDateError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private readonly Optional<SubscriptionComponentAllocationError1> _subscriptionComponentAllocationError1Value;

    private UpdatePrepaidUsageAllocationExpirationDateError(Optional<RawError> noContentValue,
        Optional<SubscriptionComponentAllocationError1> subscriptionComponentAllocationError1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
        _subscriptionComponentAllocationError1Value = subscriptionComponentAllocationError1Value;
    }

    private static UpdatePrepaidUsageAllocationExpirationDateError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default, default);

    private static UpdatePrepaidUsageAllocationExpirationDateError AsSubscriptionComponentAllocationError1(SubscriptionComponentAllocationError1 value) =>
        new(default, Optional<SubscriptionComponentAllocationError1>.Some(value), default);

    private static UpdatePrepaidUsageAllocationExpirationDateError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    public bool TryGetSubscriptionComponentAllocationError1(out SubscriptionComponentAllocationError1 value) =>
        _subscriptionComponentAllocationError1Value.TryGetValue(out value);

    internal static Task<UpdatePrepaidUsageAllocationExpirationDateError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            422 => FromJson<SubscriptionComponentAllocationError1>(response, ct).As(AsSubscriptionComponentAllocationError1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdatePrepaidUsageAllocationExpirationDateErrorResponse : IErrorResponse<UpdatePrepaidUsageAllocationExpirationDateError>
{
    public static UpdatePrepaidUsageAllocationExpirationDateErrorResponse Instance { get; } = new();

    private UpdatePrepaidUsageAllocationExpirationDateErrorResponse()
    {
    }

    public Task<UpdatePrepaidUsageAllocationExpirationDateError> Map(HttpResponseMessage response,
        CancellationToken ct) => UpdatePrepaidUsageAllocationExpirationDateError.Create(response, ct);
}
