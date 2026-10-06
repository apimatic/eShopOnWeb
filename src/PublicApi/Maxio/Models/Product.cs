using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Maxio Advanced Billing Product (a subscription plan), per the OpenAPI spec schema <c>Product</c>.
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public string? AccountingCode { get; set; }
    public bool RequestCreditCard { get; set; }
    public int? ExpirationInterval { get; set; }
    public string? ExpirationIntervalUnit { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? InitialChargeInCents { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public string? ReturnParams { get; set; }
    public bool Taxable { get; set; }
    public string? UpdateReturnUrl { get; set; }
    public bool? InitialChargeAfterTrial { get; set; }
    public int VersionNumber { get; set; }
    public string? UpdateReturnParams { get; set; }
    public ProductFamily? ProductFamily { get; set; }
    public string? ProductPricePointName { get; set; }
    public bool RequestBillingAddress { get; set; }
    public bool RequireBillingAddress { get; set; }
    public bool RequireShippingAddress { get; set; }
    public string? TaxCode { get; set; }
    public int DefaultProductPricePointId { get; set; }
    public bool? UseSiteExchangeRate { get; set; }
    public string? ItemCategory { get; set; }
    public int? ProductPricePointId { get; set; }
    public string? ProductPricePointHandle { get; set; }
}
