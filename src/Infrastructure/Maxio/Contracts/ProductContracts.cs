using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio wraps every product in a single-property envelope.</summary>
internal sealed class ProductEnvelope
{
    public ProductResource? Product { get; set; }
}

internal sealed class ProductResource
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>Whether Maxio demands a payment profile before a subscription can be created.</summary>
    public bool RequireCreditCard { get; set; }

    public string? ProductPricePointName { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public long? InitialChargeInCents { get; set; }
    public ProductFamilyResource? ProductFamily { get; set; }
}

internal sealed class ProductFamilyResource
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}
