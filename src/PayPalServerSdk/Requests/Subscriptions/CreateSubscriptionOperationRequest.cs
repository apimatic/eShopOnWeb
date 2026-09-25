using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Requests.Subscriptions;

/// <summary>
/// The inputs of the CreateSubscription operation.
/// </summary>
public sealed record CreateSubscriptionOperationRequest
{
    /// <summary>
    /// The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.
    /// </summary>
    public string Prefer { get; init; } = "return=minimal";

    /// <summary>
    /// The server stores keys for 72 hours.
    /// </summary>
    public string? PayPalRequestId { get; init; }

    /// <summary>
    /// The PayPal Client Metadata Id(CMID) is used to provide device-specific information to PayPal's risk engine. This is crucial for transactions that require device-specific risk assessments. Merchants typically use the Paypal SDK that automatically submits the CMID or they use tools like Fraudnet JS for web or Magnes JS for mobile to generate the CMID on the frontend and then pass it to the API as part of the request headers.
    /// </summary>
    [StringLength(36, MinimumLength = 1)]
    public string? PayPalClientMetadataId { get; init; }

    public CreateSubscriptionRequest? Body { get; init; }
}
