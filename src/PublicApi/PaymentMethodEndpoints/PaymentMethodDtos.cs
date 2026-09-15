using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>A safe description of a saved card — never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }

    public static PaymentMethodDto From(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Alias = pm.Alias,
        Brand = pm.Brand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry
    };
}

public class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
    public string? Alias { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class ListPaymentMethodsRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}
