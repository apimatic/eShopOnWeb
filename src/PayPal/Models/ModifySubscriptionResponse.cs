using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// The response to a request to update the quantity of the product or service in a subscription. You can also use this method to switch the plan and update the <c>shipping_amount</c> and <c>shipping_address</c> values for the subscription. This type of update requires the buyer's consent.
/// </summary>
public record ModifySubscriptionResponse
{
    /// <summary>
    /// The unique PayPal-generated ID for the plan.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plan_id")]
    [StringLength(26, MinimumLength = 26)]
    [RegularExpression("^P-[A-Z0-9]*$")]
    public string? PlanId { get; init; }

    /// <summary>
    /// The quantity of the product or service in the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quantity")]
    [StringLength(32, MinimumLength = 1)]
    [RegularExpression("^([0-9]+|([0-9]+)?[.][0-9]+)$")]
    public string? Quantity { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_amount")]
    public Money? ShippingAmount { get; init; }

    /// <summary>
    /// The shipping details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_address")]
    public ShippingDetails? ShippingAddress { get; init; }

    /// <summary>
    /// An inline plan object to customise the subscription. You can override plan level default attributes by providing customised values for the subscription in this object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plan")]
    public PlanOverride? Plan { get; init; }

    /// <summary>
    /// Indicates whether the subscription has overridden any plan attributes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plan_overridden")]
    public bool? PlanOverridden { get; init; }

    /// <summary>
    /// An array of request-related <see href="/docs/api/reference/api-responses/#hateoas-links">HATEOAS links</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public IReadOnlyList<LinkDescription>? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
