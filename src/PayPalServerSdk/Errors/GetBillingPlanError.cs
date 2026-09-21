using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class GetBillingPlanError : ApiError
{
    private readonly Optional<SubscriptionError> _subscriptionErrorValue;

    private GetBillingPlanError(Optional<SubscriptionError> subscriptionErrorValue, Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionErrorValue = subscriptionErrorValue;
    }

    private static GetBillingPlanError AsSubscriptionError(SubscriptionError value) =>
        new(Optional<SubscriptionError>.Some(value), default);

    private static GetBillingPlanError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionError(out SubscriptionError value) =>
        _subscriptionErrorValue.TryGetValue(out value);

    internal static Task<GetBillingPlanError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            401 or 403 or 404 or 500 => FromJson<SubscriptionError>(response, ct).As(AsSubscriptionError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetBillingPlanErrorResponse : IErrorResponse<GetBillingPlanError>
{
    public static GetBillingPlanErrorResponse Instance { get; } = new();

    private GetBillingPlanErrorResponse()
    {
    }

    public Task<GetBillingPlanError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetBillingPlanError.Create(response, ct);
}
