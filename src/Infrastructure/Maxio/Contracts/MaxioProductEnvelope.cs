namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio wraps each product in a single-property envelope.</summary>
public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}
