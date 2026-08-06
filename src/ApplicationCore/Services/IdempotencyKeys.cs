using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds deterministic idempotency keys for money-moving gateway calls. Two identical requests (a
/// double-click) produce the same key so the gateway de-duplicates them, while genuinely different
/// requests (a different card, a different order) produce different keys so a retry can proceed.
/// Keys carry only non-sensitive data (order id, saved-card id, card last-four and expiry) — never a full PAN.
/// </summary>
public static class IdempotencyKeys
{
    private static string Last4(string cardNumber)
    {
        var digits = new string(cardNumber.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static string Sanitize(string value)
    {
        var chars = value.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray();
        var cleaned = new string(chars);
        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }

    public static string SaveCard(string buyerId, CardDetails card)
        => $"save-{Sanitize(buyerId)}-{Last4(card.Number)}-{card.ExpiryYear:D4}{card.ExpiryMonth:D2}";

    public static string PayOrderWithCard(int orderId, CardDetails card)
        => $"pay-order-{orderId}-{Last4(card.Number)}-{card.ExpiryYear:D4}{card.ExpiryMonth:D2}";

    public static string PayOrderWithSavedCard(int orderId, int savedPaymentMethodId)
        => $"pay-order-{orderId}-pm-{savedPaymentMethodId}";

    public static string RefundOrder(int orderId)
        => $"refund-order-{orderId}";
}
