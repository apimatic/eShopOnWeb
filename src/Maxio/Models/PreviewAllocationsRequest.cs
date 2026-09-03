using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record PreviewAllocationsRequest
{
    [JsonPropertyName("allocations")]
    public required IReadOnlyList<CreateAllocation> Allocations { get; init; }

    /// <summary>
    /// To calculate proration amounts for a future time. Only within a current subscription period. Only ISO8601 format is supported.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("effective_proration_date")]
    public DateTimeOffset? EffectiveProrationDate { get; init; }

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

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
