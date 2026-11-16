using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public sealed class CreatePaymentMethodRequest
{
    public PaymentCardDto Card { get; set; } = new();
}

public sealed class ListPaymentMethodsResponse
{
    public System.Collections.Generic.IReadOnlyList<SavedCardInfo> PaymentMethods { get; set; } =
        System.Array.Empty<SavedCardInfo>();
}
