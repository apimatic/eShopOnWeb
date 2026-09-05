using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card for the signed-in shopper.</summary>
public class SavePaymentMethodRequest : ShopperRequest
{
    public PaymentCardDto? Card { get; set; }

    /// <summary>An optional name the shopper gives the card, e.g. "everyday".</summary>
    public string? Nickname { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }

    public PaymentMethodDto PaymentMethod { get; set; } = new PaymentMethodDto();
}

/// <summary>The caller's saved cards.</summary>
public class ListPaymentMethodsRequest : ShopperRequest
{
}

public class PaymentMethodListResponse : BaseResponse
{
    public PaymentMethodListResponse(Guid correlationId) : base(correlationId) { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new List<PaymentMethodDto>();
}

/// <summary>Removes one of the caller's saved cards.</summary>
public class DeletePaymentMethodRequest : ShopperRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }

    public int PaymentMethodId { get; }
}
