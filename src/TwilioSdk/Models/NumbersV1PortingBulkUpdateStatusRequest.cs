using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record NumbersV1PortingBulkUpdateStatusRequest
{
    /// <summary>
    /// The new status to set for the port in request.
    /// </summary>
    [JsonPropertyName("new_status")]
    public required NewStatus NewStatus { get; init; }

    [JsonPropertyName("port_in_phone_number_requests")]
    [MinLength(1)]
    [MaxLength(100)]
    public required IReadOnlyList<PortInPhoneNumberRequest> PortInPhoneNumberRequests { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
