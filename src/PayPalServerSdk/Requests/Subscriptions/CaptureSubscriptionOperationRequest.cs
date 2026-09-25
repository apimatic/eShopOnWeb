using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the CaptureSubscription operation.
/// </summary>
public sealed record CaptureSubscriptionOperationRequest
{
    /// <summary>
    /// The ID of the subscription.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The server stores keys for 72 hours.
    /// </summary>
    public string? PayPalRequestId { get; init; }

    public CaptureSubscriptionRequest? Body { get; init; }
}
