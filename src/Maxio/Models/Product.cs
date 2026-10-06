namespace Maxio.Models;

/// <summary>
/// A Maxio product (subscription plan). Mirrors the Product schema in the Maxio OpenAPI specification.
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public string? AccountingCode { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? InitialChargeInCents { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool RequestCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int VersionNumber { get; set; }
    public ProductFamily? ProductFamily { get; set; }
    public int? DefaultProductPricePointId { get; set; }
    public int? ProductPricePointId { get; set; }
    public string? ProductPricePointName { get; set; }
    public string? ProductPricePointHandle { get; set; }
}
