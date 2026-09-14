using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio <c>Product Family Response</c> envelope.</summary>
public class ProductFamilyResponse
{
    public ProductFamily? ProductFamily { get; set; }
}

/// <summary>Maxio <c>Product Family</c> schema.</summary>
public class ProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? AccountingCode { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
