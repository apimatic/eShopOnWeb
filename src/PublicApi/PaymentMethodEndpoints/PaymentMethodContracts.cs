using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. The card goes straight to the processor's vault — nothing
/// on this request is ever written to the application's own database or to a log.
/// </summary>
public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDto? Card { get; set; }

    public override string ToString() => "CreatePaymentMethodRequest { redacted }";
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public CreatePaymentMethodResponse() { }

    /// <summary>The identifier of the saved card. Pass it to <c>POST /api/orders/{orderId}/pay</c>.</summary>
    public int PaymentMethodId { get; set; }

    /// <summary>Enough to recognise the card — brand, last four digits, expiry. Never full details.</summary>
    public SavedCardView? PaymentMethod { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public ListPaymentMethodsResponse() { }

    public List<SavedCardView> PaymentMethods { get; set; } = new();
}
