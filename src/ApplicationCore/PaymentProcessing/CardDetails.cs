using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

// Raw card details supplied by a caller for a one-off (non-saved) card payment or to save a new
// card. Never persisted by this application beyond the lifetime of the request that carries it.
public record CardDetails(
    string Number,
    int ExpiryMonth,
    int ExpiryYear,
    string SecurityCode,
    string CardholderName,
    Address BillingAddress);
