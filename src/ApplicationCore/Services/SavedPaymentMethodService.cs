using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveAsync(
        string buyerId,
        PayPalCardInput card,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
            throw new CheckoutException(400, "Card number and expiry are required to save a payment method.");

        var lastDigits = LastDigitsFromNumber(card.Number);
        var existing = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByBuyerAndDisplaySpec(buyerId, lastDigits, card.Expiry),
            cancellationToken);
        if (existing != null)
            return existing;

        var requestId = $"vault:{buyerId}:{lastDigits}:{card.Expiry}";
        var vaulted = await _payPal.VaultCardAsync(buyerId, card, requestId, cancellationToken);
        if (string.IsNullOrEmpty(vaulted.PaymentTokenId))
            throw new CheckoutException(502, "PayPal did not return a payment token id.");

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.PayPalCustomerId,
            vaulted.LastDigits ?? lastDigits,
            vaulted.Brand,
            vaulted.Expiry ?? card.Expiry,
            vaulted.Name ?? card.Name,
            vaulted.CardType);

        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation($"Saved payment method ending {saved.LastDigits} for {buyerId}.");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return methods;
    }

    public async Task DeleteAsync(string buyerId, string paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await GetOwnedAsync(buyerId, paymentMethodId, cancellationToken);
        try
        {
            await _payPal.DeleteVaultedCardAsync(saved.PaymentTokenId, cancellationToken);
        }
        catch (CheckoutException ex) when (ex.StatusCode == 404)
        {
            // Already gone on PayPal; still drop the local row.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }

    public async Task<SavedPaymentMethod> GetOwnedAsync(string buyerId, string paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByTokenSpec(paymentMethodId), cancellationToken);
        if (saved == null || !string.Equals(saved.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
            throw new CheckoutException(404, "The saved payment method was not found.");
        return saved;
    }

    private static string LastDigitsFromNumber(string number)
    {
        var digits = number.Replace(" ", string.Empty, StringComparison.Ordinal);
        return digits.Length <= 4 ? digits : digits[^4..];
    }
}
