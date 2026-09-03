using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record RenewalPreview
{
    /// <summary>
    /// The timestamp for the subscription’s next renewal
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    /// <summary>
    /// An integer representing the amount of the total pre-tax, pre-discount charges that will be assessed at the next renewal
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subtotal_in_cents")]
    public long? SubtotalInCents { get; init; }

    /// <summary>
    /// An integer representing the total tax charges that will be assessed at the next renewal
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_tax_in_cents")]
    public long? TotalTaxInCents { get; init; }

    /// <summary>
    /// An integer representing the amount of the coupon discounts that will be applied to the next renewal
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_discount_in_cents")]
    public long? TotalDiscountInCents { get; init; }

    /// <summary>
    /// An integer representing the total amount owed, less any discounts, that will be assessed at the next renewal
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_in_cents")]
    public long? TotalInCents { get; init; }

    /// <summary>
    /// An integer representing the amount of the subscription’s current balance
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("existing_balance_in_cents")]
    public long? ExistingBalanceInCents { get; init; }

    /// <summary>
    /// An integer representing the existing_balance_in_cents plus the total_in_cents
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_amount_due_in_cents")]
    public long? TotalAmountDueInCents { get; init; }

    /// <summary>
    /// A boolean indicating whether or not additional taxes will be calculated at the time of renewal. This will be true if you are using Avalara and the address of the subscription is in one of your defined taxable regions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uncalculated_taxes")]
    public bool? UncalculatedTaxes { get; init; }

    /// <summary>
    /// An array of objects representing the individual transactions that will be created at the next renewal
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("line_items")]
    public IReadOnlyList<RenewalPreviewLineItem>? LineItems { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
