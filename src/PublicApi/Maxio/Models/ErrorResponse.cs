using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

public class CustomerErrorResponse
{
    [JsonPropertyName("errors")]
    public object? Errors { get; set; }
}

public class ErrorListResponse
{
    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}
