using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalMoney
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class PayPalCreateOrderRequest
{
    public string Intent { get; set; } = "AUTHORIZE";
    public List<PayPalPurchaseUnit> PurchaseUnits { get; set; } = new();
}

internal sealed class PayPalPurchaseUnit
{
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public string? Description { get; set; }
    public PayPalAmount Amount { get; set; } = new();
}

internal sealed class PayPalAmount
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class PayPalAuthorizeRequest
{
    public PayPalPaymentSource PaymentSource { get; set; } = new();
}

internal sealed class PayPalPaymentSource
{
    public PayPalCardPaymentSource Card { get; set; } = new();
}

internal sealed class PayPalCardPaymentSource
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayPalBillingAddress? BillingAddress { get; set; }
    public string? VaultId { get; set; }
    public PayPalCardAttributes? Attributes { get; set; }
    public PayPalStoredCredential? StoredCredential { get; set; }
}

internal sealed class PayPalCardAttributes
{
    public PayPalCardVerification? Verification { get; set; }
}

internal sealed class PayPalCardVerification
{
    public string Method { get; set; } = "SCA_WHEN_REQUIRED";
}

internal sealed class PayPalStoredCredential
{
    public string PaymentInitiator { get; set; } = "CUSTOMER";
    public string PaymentType { get; set; } = "UNSCHEDULED";
    public string Usage { get; set; } = "SUBSEQUENT";
}

internal sealed class PayPalBillingAddress
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string AdminArea2 { get; set; } = string.Empty;
    public string AdminArea1 { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

internal sealed class PayPalCaptureRequest
{
    public PayPalMoney Amount { get; set; } = new();
    public string? InvoiceId { get; set; }
    public bool FinalCapture { get; set; } = true;
}

internal sealed class PayPalReauthorizeRequest
{
    public PayPalMoney Amount { get; set; } = new();
}

internal sealed class PayPalRefundRequest
{
    public PayPalMoney? Amount { get; set; }
}

internal sealed class PayPalVaultRequest
{
    public PayPalVaultPaymentSource PaymentSource { get; set; } = new();
    public PayPalVaultCustomer? Customer { get; set; }
}

internal sealed class PayPalVaultPaymentSource
{
    public PayPalVaultCard Card { get; set; } = new();
}

internal sealed class PayPalVaultCard
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayPalBillingAddress? BillingAddress { get; set; }
}

internal sealed class PayPalVaultCustomer
{
    public string? Id { get; set; }

    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}

internal sealed class PayPalTokenResponse
{
    public string? AccessToken { get; set; }
    public int ExpiresIn { get; set; }
    public string? TokenType { get; set; }
}
