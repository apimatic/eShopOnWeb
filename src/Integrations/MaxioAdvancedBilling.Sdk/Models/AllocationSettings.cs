using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace MaxioAdvancedBilling.Models;

public record AllocationSettings
{
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
    /// Either "true" or "false".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("accrue_charge")]
    public string? AccrueCharge { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
