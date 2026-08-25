namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>One-off card details. Provide this OR <see cref="PaymentMethodId"/>, not both.</summary>
    public CardPaymentRequest? Card { get; set; }

    /// <summary>Id of a saved card (see payment-methods endpoints). Provide this OR <see cref="Card"/>, not both.</summary>
    public int? PaymentMethodId { get; set; }
}
