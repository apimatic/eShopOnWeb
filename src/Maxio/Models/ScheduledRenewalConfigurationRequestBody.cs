using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ScheduledRenewalConfigurationRequestBody
{
    /// <summary>
    /// (Optional) Start of the renewal term.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("starts_at")]
    public DateTimeOffset? StartsAt { get; init; }

    /// <summary>
    /// (Optional) End of the renewal term.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ends_at")]
    public DateTimeOffset? EndsAt { get; init; }

    /// <summary>
    /// (Optional) Lock-in date for the renewal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("lock_in_at")]
    public DateTimeOffset? LockInAt { get; init; }

    /// <summary>
    /// (Optional) Existing contract to associate with the scheduled renewal. Contracts must be enabled for your site.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("contract_id")]
    public int? ContractId { get; init; }

    /// <summary>
    /// (Optional) Set to true to create a new contract when contracts are enabled. Contracts must be enabled for your site.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("create_new_contract")]
    public bool? CreateNewContract { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
