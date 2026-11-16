using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>One-off card details. Omit when paying with a saved card.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>The shopper's saved card id (from POST /api/payment-methods).</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>Alias for PaymentMethodId.</summary>
    public int? SavedPaymentMethodId { get; set; }

    /// <summary>Optional caller idempotency key; a deterministic key is used when omitted.</summary>
    public string? IdempotencyKey { get; set; }

    // Convenience aliases so a one-off card can also be supplied at the top level.
    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardAddressRequest? Address { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    public CardRequest? ResolveCard()
    {
        if (Card != null)
        {
            return Card;
        }

        if (string.IsNullOrWhiteSpace(Number) && string.IsNullOrWhiteSpace(Name) &&
            !ExpirationMonth.HasValue && !ExpirationYear.HasValue && string.IsNullOrWhiteSpace(SecurityCode))
        {
            return null;
        }

        return new CardRequest
        {
            Number = Number ?? string.Empty,
            ExpirationMonth = this.ExpirationMonth ?? 0,
            ExpirationYear = this.ExpirationYear ?? 0,
            SecurityCode = SecurityCode ?? string.Empty,
            Name = Name ?? string.Empty,
            Address = this.Address
        };
    }

    public int? ResolvePaymentMethodId() => PaymentMethodId ?? SavedPaymentMethodId;
}
