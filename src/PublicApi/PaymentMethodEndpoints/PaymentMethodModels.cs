using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
}

public class PaymentMethodsRequest : BaseRequest
{
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; init; }

    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFour { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? CardType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardDto From(SavedCardView card)
    {
        return new SavedCardDto
        {
            PaymentMethodId = card.PaymentMethodId,
            Brand = card.Brand,
            LastFour = card.LastFour,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            CardType = card.CardType,
            CreatedAt = card.CreatedAt
        };
    }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public SavedCardDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string Status { get; set; } = "Deleted";
}
