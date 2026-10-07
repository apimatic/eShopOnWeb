using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DownloadOrderItemRequest : BaseRequest
{
    public DownloadOrderItemRequest(int orderId, int catalogItemId, string buyerId, CancellationToken cancellationToken)
    {
        OrderId = orderId;
        CatalogItemId = catalogItemId;
        BuyerId = buyerId;
        CancellationToken = cancellationToken;
    }

    public int OrderId { get; }
    public int CatalogItemId { get; }

    /// <summary>Taken from the caller's token.</summary>
    public string BuyerId { get; }

    public CancellationToken CancellationToken { get; }
}
