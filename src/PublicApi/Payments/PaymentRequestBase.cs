using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Base for payment requests. <see cref="BuyerId"/> and <see cref="Cancellation"/> are populated server-side
/// from the JWT and the request lifetime — they are never bound from the request body (a caller cannot spoof
/// another shopper's identity).
/// </summary>
public abstract class PaymentRequestBase : BaseRequest
{
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public CancellationToken Cancellation { get; set; }
}
