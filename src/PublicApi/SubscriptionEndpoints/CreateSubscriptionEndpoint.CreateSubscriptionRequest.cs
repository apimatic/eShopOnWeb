using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, from GET /api/subscription-plans. Optional only when the
    /// deployment configures Maxio:DefaultPlanHandle.
    /// </summary>
    [StringLength(100, MinimumLength = 1)]
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional key that makes this subscribe request safe to retry: repeating a request with the
    /// same key returns the subscription the first attempt created instead of creating another.
    /// May also be sent as the <c>Idempotency-Key</c> header.
    /// </summary>
    [StringLength(64, MinimumLength = 1)]
    public string? IdempotencyKey { get; set; }

    /// <summary>Optional shopper first name, used only when the billing customer is first created.</summary>
    [StringLength(100)]
    public string? FirstName { get; set; }

    /// <summary>Optional shopper last name, used only when the billing customer is first created.</summary>
    [StringLength(100)]
    public string? LastName { get; set; }

    /// <summary>
    /// The authenticated user this subscription is for. Populated from the bearer token by the
    /// endpoint and never bound from the request body.
    /// </summary>
    [JsonIgnore]
    public string UserName { get; set; } = string.Empty;
}
