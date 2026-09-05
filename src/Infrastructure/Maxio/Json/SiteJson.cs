namespace Microsoft.eShopWeb.Infrastructure.Maxio.Json;

internal sealed class SiteEnvelope
{
    public SiteJson? Site { get; set; }
}

internal sealed class SiteJson
{
    public string Currency { get; set; } = string.Empty;
}
