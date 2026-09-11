using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse this shopper's existing PayPal customer id so all their cards are grouped.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        var customerId = existing.Select(m => m.PayPalCustomerId).FirstOrDefault(id => id is not null);

        VaultedCard vaulted;
        try
        {
            vaulted = await _payPal.VaultCardAsync(card, customerId, $"vault-{Guid.NewGuid():N}", ct);
        }
        catch (PayPalApiException ex)
        {
            throw PaymentException.Declined($"The card could not be saved: {ex.Message}", ex.Issues);
        }

        var method = new SavedPaymentMethod(
            buyerId, vaulted.TokenId, vaulted.CustomerId ?? customerId,
            vaulted.Brand, vaulted.LastDigits, vaulted.ExpiryMonthYear, vaulted.Name);

        method = await _repository.AddAsync(method, ct);
        _logger.LogInformation($"Saved card {method.Id} (vault token {vaulted.TokenId}) for buyer.");
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        return cards.OrderByDescending(c => c.CreatedAt).ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var method = await _repository.GetByIdAsync(paymentMethodId, ct);
        if (method is null || method.BuyerId != buyerId)
            throw PaymentException.NotFound("The specified saved payment method was not found.");

        // Remove it from PayPal's vault so it can no longer be charged, then from our store.
        try
        {
            await _payPal.DeleteVaultedCardAsync(method.PayPalVaultId, ct);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning($"PayPal vault delete for token {method.PayPalVaultId} returned {ex.Name}: {ex.Message}. Removing locally regardless.");
        }

        await _repository.DeleteAsync(method, ct);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} for buyer.");
    }
}
