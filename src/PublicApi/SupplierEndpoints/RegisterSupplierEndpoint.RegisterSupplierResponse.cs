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

    /// <summary>Identifies the registered supplier.</summary>
    public int SupplierId { get; set; }

    public string Name { get; set; }

    public string ProductListingUrl { get; set; }
}
