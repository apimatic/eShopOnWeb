using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class ListBillingPlansError : ApiError
{
    private readonly Optional<SubscriptionError> _subscriptionErrorValue;

    private ListBillingPlansError(Optional<SubscriptionError> subscriptionErrorValue, Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionErrorValue = subscriptionErrorValue;
    }

    private static ListBillingPlansError AsSubscriptionError(SubscriptionError value) =>
        new(Optional<SubscriptionError>.Some(value), default);

    private static ListBillingPlansError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionError(out SubscriptionError value) =>
        _subscriptionErrorValue.TryGetValue(out value);

    internal static Task<ListBillingPlansError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 403 or 404 or 500 => FromJson<SubscriptionError>(response, ct).As(AsSubscriptionError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListBillingPlansErrorResponse : IErrorResponse<ListBillingPlansError>
{
    public static ListBillingPlansErrorResponse Instance { get; } = new();

    private ListBillingPlansErrorResponse()
    {
    }

    public Task<ListBillingPlansError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListBillingPlansError.Create(response, ct);
}
