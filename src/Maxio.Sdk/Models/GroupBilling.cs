using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// (Optional) Attributes related to billing date and accrual. Note: Only applicable for new subscriptions.
/// </summary>
public record GroupBilling
{
    /// <summary>
    /// A flag indicating whether or not to accrue charges on the new subscription.
    /// </summary>
    [JsonPropertyName("accrue")]
    public bool? Accrue { get; init; } = false;

    /// <summary>
    /// A flag indicating whether or not to align the billing date of the new subscription with the billing date of the primary subscription of the hierarchy's default subscription group. Required to be true if prorate is also true.
    /// </summary>
    [JsonPropertyName("align_date")]
    public bool? AlignDate { get; init; } = false;

    /// <summary>
    /// A flag indicating whether or not to prorate billing of the new subscription for the current period. A value of true is ignored unless align_date is also true.
    /// </summary>
    [JsonPropertyName("prorate")]
    public bool? Prorate { get; init; } = false;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
