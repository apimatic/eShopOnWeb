using Microsoft.eShopWeb.PublicApi.PaymentDtos;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodsEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. Only the card is sent once; the response
/// carries safe-to-display information only.
/// </summary>
public class SavePaymentMethodRequest : BaseRequest
{
    public PaymentCardDto Card { get; set; } = new();
}
