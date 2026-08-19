namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Tunables for the supplier-catalog sync: how the listing is read via Firecrawl and how the
/// resulting products are slotted into the store's catalog taxonomy.
/// </summary>
public class SupplierSyncOptions
{
    /// <summary>How often to poll the Firecrawl extract job for completion.</summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>Maximum time to wait for a Firecrawl extract job to finish before giving up.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Catalog type assigned to imported items (created on first use if missing).</summary>
    public string CatalogTypeName { get; set; } = "Supplier Catalog";

    /// <summary>Brand assigned when a product's brand is missing (created on first use if missing).</summary>
    public string DefaultBrandName { get; set; } = "Unbranded";

    /// <summary>Placeholder image assigned to imported items.</summary>
    public string DefaultPictureName { get; set; } = "eCatalog-item-default.png";

    /// <summary>Prompt handed to Firecrawl's extractor to shape how products are read off the page.</summary>
    public string ExtractionPrompt { get; set; } =
        "Extract every product listed on this supplier's catalog page(s), following pagination. " +
        "Return one entry per product. For each product capture its name, full description, brand, " +
        "and the supplier's own SKU/product code. For price, put the numeric amount in priceAmount " +
        "when a number is shown and copy the exact on-page price text into priceText. If a product " +
        "shows no numeric price (for example 'Contact for pricing'), set priceAmount to null but " +
        "still include the product. Also capture the product's page URL in url when available.";
}
