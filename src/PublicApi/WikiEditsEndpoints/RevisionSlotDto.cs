namespace Microsoft.eShopWeb.PublicApi.WikiEditsEndpoints;

public class RevisionSlotDto
{
    public string Name { get; set; } = string.Empty;
    public string ContentModel { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
}
