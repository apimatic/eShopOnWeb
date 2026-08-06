namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodResponse : BaseResponse
{
    /// <summary>Identifier of the newly saved card (top-level, so the flow can be driven end to end).</summary>
    public int PaymentMethodId { get; set; }

    public PaymentMethodDto PaymentMethod { get; set; } = new();
}
