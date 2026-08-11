using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Manages a shopper's vaulted cards. Card details are vaulted with PayPal and never persisted
/// by the application; only the vault token and a safe descriptor are stored.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.Expiry,
            card.CardholderName,
            label);

        saved = await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation($"Saved card {saved.Id} ({vaulted.Brand} ****{vaulted.Last4}) vaulted for buyer.");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _repository.ListAsync(new CustomerSavedPaymentMethodsSpecification(buyerId), cancellationToken);
        return cards;
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId), cancellationToken);
        if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentNotFoundException($"Saved card {paymentMethodId} was not found.");

        // Remove from the vault first so it can no longer be used to pay, then locally.
        await _gateway.DeleteVaultedCardAsync(saved.VaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);

        _logger.LogInformation($"Saved card {paymentMethodId} deleted for buyer.");
    }
}
