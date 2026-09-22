using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();

    [JsonIgnore] public string? BuyerId { get; set; }
    [JsonIgnore] public CancellationToken Cancellation { get; set; }
}

/// <summary>Context-only request for the list/delete endpoints (no JSON body).</summary>
public class PaymentMethodOperationRequest
{
    public int PaymentMethodId { get; set; }
    public string? BuyerId { get; set; }
    public CancellationToken Cancellation { get; set; }
}

public class SavedPaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static SavedPaymentMethodResponse From(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
    };
}
