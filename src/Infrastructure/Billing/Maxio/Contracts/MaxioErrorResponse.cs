using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>
/// Maxio's validation error body, returned as <c>{"errors": ["Reference: must be unique - ..."]}</c>
/// alongside HTTP 422.
/// </summary>
public class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}
