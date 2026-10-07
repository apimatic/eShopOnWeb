namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// One content slot of a revision (e.g. "main", or the structured-data slot of a Commons file page).
/// </summary>
public class WikiEditSlotDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Content model of the slot; null only if Wikimedia sent a slot without one.
    /// </summary>
    public string? ContentModel { get; set; }

    /// <summary>
    /// Size of the slot content in bytes; null only if Wikimedia sent a slot without one.
    /// </summary>
    public long? SizeBytes { get; set; }
}
