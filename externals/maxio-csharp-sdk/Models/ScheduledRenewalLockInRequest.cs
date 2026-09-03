using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ScheduledRenewalLockInRequest
{
    /// <summary>
    /// Date to lock in the renewal.
    /// </summary>
    [JsonPropertyName("lock_in_at")]
    public required DateTimeOffset LockInAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
