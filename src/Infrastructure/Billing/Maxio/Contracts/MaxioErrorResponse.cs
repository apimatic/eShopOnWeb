using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>
/// Maxio's error body, e.g. <c>{"errors":["Reference: must be unique - that value has been taken."]}</c>.
/// </summary>
internal sealed class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}
