using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A Maxio product family. Mirrors the <c>Product-Family.yaml</c> schema from the Maxio OpenAPI spec.
/// </summary>
public class MaxioProductFamily
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }
}
