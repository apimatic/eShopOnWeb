using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>The card to save. Full card details are forwarded to PayPal and never stored by this app.</summary>
    public CardDto Card { get; set; } = new();
}

/// <summary>A saved card, described safely (brand, last four, expiry) so a shopper can recognise it.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? Expiry { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
