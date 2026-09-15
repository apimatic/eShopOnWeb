using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPalGateway;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPalGateway)
    {
        _repository = repository;
        _payPalGateway = payPalGateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));
        Guard.Against.NullOrEmpty(card.Number, nameof(card.Number));
        Guard.Against.NullOrEmpty(card.Expiry, nameof(card.Expiry));
        Guard.Against.NullOrEmpty(card.SecurityCode, nameof(card.SecurityCode));
        Guard.Against.NullOrEmpty(card.Name, nameof(card.Name));
        if (card.BillingAddress == null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
        {
            throw new PaymentException("Card billing address with a country code is required.");
        }

        var vaulted = await _payPalGateway.VaultCardAsync(
            card,
            SanitizeMerchantCustomerId(buyerId),
            payPalRequestId: $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.Name);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId),
            cancellationToken);
        if (saved == null)
        {
            throw new PaymentNotFoundException($"Saved payment method {paymentMethodId} was not found for this shopper.");
        }

        try
        {
            await _payPalGateway.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentNotFoundException)
        {
            // Already removed at PayPal; still drop the local record so it cannot be reused.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var sanitized = new string(buyerId.Where(ch =>
            char.IsLetterOrDigit(ch) || "-_.^*$@#".Contains(ch)).ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "shopper";
        }

        return sanitized.Length <= 64 ? sanitized : sanitized[..64];
    }
}
