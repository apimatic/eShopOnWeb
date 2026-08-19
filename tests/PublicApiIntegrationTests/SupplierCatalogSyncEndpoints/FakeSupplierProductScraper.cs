using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.SupplierCatalogSyncEndpoints;

/// <summary>
/// Deterministic stand-in for the Firecrawl-backed scraper so the sync flow can be tested
/// without any network. Returns three products: two fully importable, and one missing a price
/// (so the sync lands on "partially completed" — found 3, imported 2).
/// </summary>
public class FakeSupplierProductScraper : ISupplierProductScraper
{
    public const string ProductAName = "Fixture Widget A";
    public const string ProductBName = "Fixture Widget B";
    public const string ProductCName = "Fixture Widget C (no price)";

    private static readonly IReadOnlyList<ScrapedProduct> Catalog = new List<ScrapedProduct>
    {
        new("https://supplier.example/p/1", ProductAName, "A sturdy widget", 19.99m, "Acme"),
        new("https://supplier.example/p/2", ProductBName, "A deluxe widget", 29.50m, "Globex"),
        new("https://supplier.example/p/3", ProductCName, "This one has no price", null, "Acme"),
    };

    public Task<SupplierScrapeResult> ScrapeListingAsync(string listingUrl, CancellationToken cancellationToken) =>
        Task.FromResult(new SupplierScrapeResult(Catalog));
}
