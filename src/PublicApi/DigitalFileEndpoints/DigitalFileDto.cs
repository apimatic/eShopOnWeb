namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

public class DigitalFileDto
{
    /// <summary>The file's Box id.</summary>
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Size in bytes, when Box reported one.</summary>
    public long? Size { get; set; }
    /// <summary>SHA-1 of the file content as reported by Box; lets operators verify a download.</summary>
    public string? Sha1 { get; set; }
}
