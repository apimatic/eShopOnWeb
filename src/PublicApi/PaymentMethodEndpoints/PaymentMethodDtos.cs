using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(SavedCard card)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand,
            Last4 = card.LastFour,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Name = card.Name,
            CreatedAt = card.CreatedAt
        };
    }
}
