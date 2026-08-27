using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Raw card details for a one-off payment. Never stored.</summary>
    public CardDetails? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards (from POST /api/payment-methods).</summary>
    public int? SavedPaymentMethodId { get; set; }
}
