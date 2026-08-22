using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly ILogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPal,
        ILogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedCard> SaveAsync(string buyerId, CardPaymentRequest card, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", HttpStatusCode.Unauthorized);
        }

        ValidateCard(card);

        var vaulted = await _payPal.VaultCardAsync(
            new PayPalCardDetails
            {
                Name = card.Name,
                Number = NormalizeCardNumber(card.Number),
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = new PayPalBillingAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
            },
            SanitizeMerchantCustomerId(buyerId),
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var lastDigits = vaulted.LastDigits ?? LastDigitsFrom(card.Number);
        var saved = new SavedCard(
            buyerId,
            vaulted.PaymentTokenId,
            lastDigits,
            vaulted.Brand ?? "CARD",
            vaulted.Expiry ?? card.Expiry,
            vaulted.CardholderName ?? card.Name,
            vaulted.CustomerId);

        await _savedCardRepository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Vaulted a payment method ending {LastDigits} for buyer {BuyerId}.", lastDigits, buyerId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await GetOwnedAsync(buyerId, paymentMethodId, cancellationToken);
        try
        {
            await _payPal.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning(
                "PayPal did not delete vault token {TokenId} for payment method {PaymentMethodId}: {Message}",
                saved.PayPalPaymentTokenId,
                saved.Id,
                ex.Message);
        }

        saved.Remove();
        await _savedCardRepository.UpdateAsync(saved, cancellationToken);
    }

    public async Task<SavedCard> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdSpec(paymentMethodId, buyerId),
            cancellationToken);
        if (saved is null)
        {
            throw new PaymentException("The saved card was not found.", HttpStatusCode.NotFound);
        }

        return saved;
    }

    private static void ValidateCard(CardPaymentRequest card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) ||
            string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) ||
            string.IsNullOrWhiteSpace(card.SecurityCode) ||
            card.BillingAddress is null ||
            string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
        {
            throw new PaymentException("Card name, number, expiry, security code, and billing countryCode are required.");
        }
    }

    private static string NormalizeCardNumber(string number) =>
        new string(number.Where(char.IsDigit).ToArray());

    private static string LastDigitsFrom(string number)
    {
        var digits = NormalizeCardNumber(number);
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var sanitized = buyerId.Length <= 64 ? buyerId : buyerId[..64];
        return sanitized;
    }
}
