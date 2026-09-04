using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodsEndpoints;

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse() { }

    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>Top-level identifier of the created saved card.</summary>
    public string PaymentMethodId { get; set; } = string.Empty;

    public string? Brand { get; set; }

    public string? Last4 { get; set; }

    public string? Expiry { get; set; }

    public string? CardholderName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
