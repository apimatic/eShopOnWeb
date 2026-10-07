using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// What the shopper is told about a payment, in terms they can act on. The provider's own refusal reason is
/// passed through (it is written for humans) together with the action that fixes the most likely cause.
/// </summary>
public static class ShopperMessages
{
    public const string Paid = "Payment successful. Thank you for your order.";

    public const string Pending =
        "Your payment is being processed by your card issuer. Please do not pay again; your order will be updated once it is confirmed.";

    public const string Unknown =
        "We could not confirm your payment. Please try paying again in a moment — you will not be charged twice for this order.";

    public const string ActionRequired =
        "Your card requires an additional verification step (such as 3-D Secure) that is not supported here. No money was taken. Please use a different card.";

    public const string ProviderUnavailable =
        "We cannot take card payments right now. No money was taken. Please try again later.";

    public static string ForRefusal(string? refusalReason)
    {
        var reason = string.IsNullOrWhiteSpace(refusalReason) ? null : refusalReason.Trim();
        var advice = AdviceFor(reason);
        return reason is null
            ? $"Your card was declined. No money was taken. {advice}"
            : $"Your card was declined ({reason}). No money was taken. {advice}";
    }

    public static string ForFailure(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "The payment could not be completed. No money was taken. Please try again or use a different card."
            : $"The payment could not be completed ({reason.Trim()}). No money was taken. Please try again or use a different card.";

    public static string ForInvalidCard(string? providerMessage) =>
        string.IsNullOrWhiteSpace(providerMessage)
            ? "Your card details could not be processed. No money was taken. Please re-enter your card details."
            : $"Your card details could not be processed ({providerMessage.Trim()}). No money was taken. Please re-enter your card details.";

    private static string AdviceFor(string? reason)
    {
        if (reason is null)
            return "Please use a different card or contact your card issuer.";

        bool Has(string fragment) => reason.Contains(fragment, StringComparison.OrdinalIgnoreCase);

        if (Has("cvc") || Has("cvv") || Has("security code"))
            return "Please check the security code on the back of your card and try again.";
        if (Has("expir"))
            return "Please check the expiry date, or use a different card.";
        if (Has("balance") || Has("funds") || Has("limit"))
            return "Please use a different card, or contact your card issuer about your available funds.";
        if (Has("pin") || Has("number") || Has("invalid card"))
            return "Please check your card details and try again.";
        if (Has("fraud") || Has("blocked") || Has("restricted") || Has("not supported") || Has("not allowed"))
            return "Please use a different card or contact your card issuer.";
        return "Please try again, use a different card, or contact your card issuer.";
    }
}
