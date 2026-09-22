using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Saves and manages a shopper's vaulted cards. Vaulting is delegated to PayPal (which holds the card);
/// eShop keeps only the token id, a safe descriptor, and the owning buyer id.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly ILogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentGateway gateway,
        ILogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardInput card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
            throw new PaymentOperationException(PaymentOperationErrorKind.Validation, "Card number and expiry (YYYY-MM) are required.");

        // Reuse the shopper's existing PayPal customer id, if any, so all their cards group under one customer.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        var existingCustomerId = existing.Select(m => m.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var command = new PayPalVaultCardCommand
        {
            BuyerId = buyerId,
            PayPalCustomerId = existingCustomerId,
            MerchantCustomerId = existingCustomerId is null ? SanitizeMerchantCustomerId(buyerId) : null,
            Card = new PayPalCardDetails
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                CardholderName = card.CardholderName,
                BillingAddress = BuildBillingAddress(card),
            },
        };

        var result = await _gateway.VaultCardAsync(command, ct);

        var saved = new SavedPaymentMethod(buyerId, result.VaultId, result.PayPalCustomerId,
            result.CardBrand, result.LastFourDigits, result.Expiry ?? card.Expiry, card.CardholderName);
        await _repository.AddAsync(saved, ct);

        _logger.LogInformation("Saved card {PaymentMethodId} for {BuyerId} ({Brand} ****{Last4})",
            saved.Id, buyerId, saved.CardBrand, saved.LastFourDigits);
        return ToView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        return cards.Select(ToView).ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var card = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), ct);
        if (card is null)
            throw new PaymentOperationException(PaymentOperationErrorKind.NotFound, $"Saved payment method {paymentMethodId} was not found for this shopper.");

        // Remove from PayPal's vault first so it can no longer be used, then drop the local row.
        await _gateway.DeleteVaultedCardAsync(card.PayPalVaultId, ct);
        await _repository.DeleteAsync(card, ct);

        _logger.LogInformation("Deleted saved card {PaymentMethodId} for {BuyerId}", paymentMethodId, buyerId);
    }

    private static PayPalBillingAddress? BuildBillingAddress(CardInput card) =>
        (card.BillingAddressLine1 ?? card.BillingCity ?? card.BillingPostalCode ?? card.BillingCountryCode) is null
            ? null
            : new PayPalBillingAddress
            {
                AddressLine1 = card.BillingAddressLine1,
                AdminArea2 = card.BillingCity,
                AdminArea1 = card.BillingState,
                PostalCode = card.BillingPostalCode,
                CountryCode = card.BillingCountryCode,
            };

    // merchant_customer_id allows [0-9a-zA-Z-_.^*$@#]; strip anything else from the buyer id.
    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var cleaned = Regex.Replace(buyerId, "[^0-9a-zA-Z\\-_.^*$@#]", "-");
        return cleaned.Length > 64 ? cleaned.Substring(0, 64) : cleaned;
    }

    private static SavedCardView ToView(SavedPaymentMethod m) => new()
    {
        PaymentMethodId = m.Id,
        CardBrand = m.CardBrand,
        LastFourDigits = m.LastFourDigits,
        Expiry = m.Expiry,
        CardholderName = m.CardholderName,
        CreatedAt = m.CreatedAt,
    };
}
