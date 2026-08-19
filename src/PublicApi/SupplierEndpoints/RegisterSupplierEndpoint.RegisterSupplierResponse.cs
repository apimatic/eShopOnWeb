using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class RegisterSupplierResponse : BaseResponse
{
    public RegisterSupplierResponse(Guid correlationId) : base(correlationId) { }

    public RegisterSupplierResponse() { }

    /// <summary>The registered supplier's identifier.</summary>
    public Guid SupplierId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ListingUrl { get; set; } = string.Empty;
}
