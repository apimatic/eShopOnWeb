namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

public class DigitalFileDto
{
    public string BoxFileId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? Size { get; set; }
}

public class UnreadableDigitalFileDto
{
    public string? BoxFileId { get; set; }
    public string? Name { get; set; }
    public string Reason { get; set; } = string.Empty;
}
