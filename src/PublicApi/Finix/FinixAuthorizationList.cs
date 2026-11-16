using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public sealed class FinixAuthorizationList
{
    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixAuthorizationEmbedded? Embedded { get; set; }
}

public sealed class FinixAuthorizationEmbedded
{
    [JsonPropertyName("authorizations")]
    public List<FinixAuthorization>? Authorizations { get; set; }
}
