using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionMigrationPreview
{
    /// <summary>
    /// The amount of the prorated adjustment that would be issued for the current subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prorated_adjustment_in_cents")]
    public long? ProratedAdjustmentInCents { get; init; }

    /// <summary>
    /// The amount of the charge that would be created for the new product.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("charge_in_cents")]
    public long? ChargeInCents { get; init; }

    /// <summary>
    /// The amount of the payment due in the case of an upgrade.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_due_in_cents")]
    public long? PaymentDueInCents { get; init; }

    /// <summary>
    /// Represents a credit in cents that is applied to your subscription as part of a migration process for a specific product, which reduces the amount owed for the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("credit_applied_in_cents")]
    public long? CreditAppliedInCents { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
