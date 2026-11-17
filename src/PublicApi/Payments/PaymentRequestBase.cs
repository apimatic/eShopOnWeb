using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Base for payment requests. The caller identity, admin flag and cancellation token are filled
/// from the HTTP context and are never accepted from the request body.
/// </summary>
public abstract class PaymentRequestBase : BaseRequest
{
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsAdmin { get; set; }

    [JsonIgnore]
    public CancellationToken RequestAborted { get; set; }
}
