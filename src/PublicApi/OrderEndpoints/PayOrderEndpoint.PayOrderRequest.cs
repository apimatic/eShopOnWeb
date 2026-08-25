namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Body of POST api/orders/{orderId}/pay. Exactly one of Card or PaymentMethodId must be supplied.</summary>
public class PayOrderRequestBody
{
    public CardDetailsRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

/// <summary>
/// The saved-card lookup (if any) is resolved by the route handler itself, from a repository bound
/// per-request, before this request object is built — see the note on PayOrderEndpoint for why.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public PayOrderRequest(int orderId, CardDetailsRequest? card, string? vaultId)
    {
        OrderId = orderId;
        Card = card;
        VaultId = vaultId;
    }

    public int OrderId { get; }
    public CardDetailsRequest? Card { get; }
    public string? VaultId { get; }
}
