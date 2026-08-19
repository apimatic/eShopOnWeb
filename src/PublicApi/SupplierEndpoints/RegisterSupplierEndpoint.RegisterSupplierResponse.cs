using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class RegisterSupplierResponse : BaseResponse
{
    public RegisterSupplierResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RegisterSupplierResponse()
    {
    }

    /// <summary>Identifies the newly registered supplier.</summary>
    public int SupplierId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ProductListingUrl { get; set; } = string.Empty;
}
