using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record PayPalAddress(string AddressLine1, string? AddressLine2, string City,
    string? State, string PostalCode, string CountryCode);

public sealed record PayPalCard(string Name, string Number, string Expiry, string SecurityCode,
    PayPalAddress BillingAddress);

public sealed record PayPalOrderItem(int CatalogItemId, string Name, decimal UnitPrice, int Quantity);

public sealed record PayPalOrderResult(string Id, string Status);

public sealed record PayPalAuthorizationResult(string Id, string Status, decimal Amount,
    string Currency, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt);

public sealed record PayPalCaptureResult(string Id, string Status, decimal Amount, string Currency,
    decimal? PayPalFee, decimal? NetAmount, DateTimeOffset? CreatedAt);

public sealed record PayPalRefundResult(string Id, string Status, decimal Amount, string Currency,
    decimal? PayPalFeeRefunded, decimal? NetAmountDebited);

public sealed record PayPalVaultedCardResult(string Id, string Brand, string Last4, string Expiry);

public sealed record PayPalTransactionResult(string Id, string? ReferenceId, string? EventCode,
    string? Status, DateTimeOffset? InitiatedAt, decimal Amount, string Currency, decimal? Fee,
    string? InvoiceId, string? CustomId);

public sealed record PayPalTransactionPage(IReadOnlyCollection<PayPalTransactionResult> Transactions,
    int Page, int TotalPages);
