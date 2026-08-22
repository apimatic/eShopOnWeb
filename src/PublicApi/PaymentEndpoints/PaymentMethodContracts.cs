using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequest Card { get; set; } = new();
}

public class PaymentMethodResponse : BaseResponse
{
    public PaymentMethodResponse() { }
    public PaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public static PaymentMethodResponse From(SavedPaymentMethod method, System.Guid? correlationId = null)
    {
        var brand = string.IsNullOrWhiteSpace(method.Brand) ? "Card" : method.Brand;
        var response = correlationId.HasValue
            ? new PaymentMethodResponse(correlationId.Value)
            : new PaymentMethodResponse();
        response.PaymentMethodId = method.Id;
        response.Brand = method.Brand;
        response.LastDigits = method.LastDigits;
        response.Expiry = method.Expiry;
        response.CardholderName = method.CardholderName;
        response.DisplayName = $"{brand} ending in {method.LastDigits} (exp {method.Expiry})";
        return response;
    }
}
