using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record AllocateComponents
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("proration_upgrade_scheme")]
    public string? ProrationUpgradeScheme { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("proration_downgrade_scheme")]
    public string? ProrationDowngradeScheme { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("allocations")]
    public IReadOnlyList<CreateAllocation>? Allocations { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("accrue_charge")]
    public bool? AccrueCharge { get; init; }

    /// <summary>
    /// The type of credit to be created when upgrading/downgrading. Defaults to the component and then site setting if one is not provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("upgrade_charge")]
    public CreditType? UpgradeCharge { get; init; }

    /// <summary>
    /// The type of credit to be created when upgrading/downgrading. Defaults to the component and then site setting if one is not provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("downgrade_credit")]
    public CreditType? DowngradeCredit { get; init; }

    /// <summary>
    /// (Optional) If not passed, the allocation(s) will use the payment collection method on the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_collection_method")]
    public CollectionMethod? PaymentCollectionMethod { get; init; }

    /// <summary>
    /// If true, if the immediate component payment fails, initiate dunning for the subscription.
    /// Otherwise, leave the charges on the subscription to pay for at renewal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("initiate_dunning")]
    public bool? InitiateDunning { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
