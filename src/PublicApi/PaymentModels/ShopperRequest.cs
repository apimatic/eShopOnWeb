namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Base for shopper-scoped requests. <see cref="CallerId"/> is always populated
/// server-side from the JWT (never from the request body), so a shopper can only
/// ever act on their own data.
/// </summary>
public abstract class ShopperRequest : BaseRequest
{
    public string CallerId { get; set; } = string.Empty;
}
