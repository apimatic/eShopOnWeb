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

    /// <summary>Identifier of the newly registered supplier.</summary>
    public int SupplierId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string ListingUrl { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}
