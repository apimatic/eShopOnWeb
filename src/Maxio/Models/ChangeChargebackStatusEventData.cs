using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

/// <summary>
/// Example schema for an <c>change_chargeback_status</c> event
/// </summary>
public record ChangeChargebackStatusEventData
{
    [JsonPropertyName("chargeback_status")]
    public required ChargebackStatus ChargebackStatus { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
