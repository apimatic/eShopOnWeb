using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class CreateInvoiceResponse : BaseResponse
{
    public CreateInvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateInvoiceResponse()
    {
    }

    /// <summary>The identifier of the raised bill, which the later invoice endpoints act on.</summary>
    public int InvoiceId { get; set; }

    /// <summary>eShop's lifecycle status of the bill (a freshly raised bill is a draft).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's identifier for the bill.</summary>
    public string ProviderInvoiceId { get; set; } = string.Empty;
}
