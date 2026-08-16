using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse an existing PayPal customer id for this shopper so all their cards
        // are grouped under one customer at PayPal.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        string? customerId = existing.Count > 0 ? existing[0].PayPalCustomerId : null;

        // A fresh reference per save: vaulting the same card twice is a legitimate distinct
        // save, and a unique key avoids colliding with any earlier run's cached request.
        var idempotencyKey = $"eshop-vault-{Guid.NewGuid():N}";
        var vaulted = await _gateway.VaultCardAsync(card, customerId, idempotencyKey, cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.CardBrand, vaulted.CardLast4, vaulted.CardExpiry, card.CardholderName);
        await _repository.AddAsync(saved, cancellationToken);

        _logger.LogInformation($"Saved card {saved.Describe()} for buyer {buyerId} (vault {vaulted.VaultId}).");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpecification(paymentMethodId, buyerId), cancellationToken)
            ?? throw new PaymentMethodNotFoundException(paymentMethodId);

        // Remove at PayPal first so the token can no longer be used to pay, then locally.
        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);

        _logger.LogInformation($"Deleted saved card {paymentMethodId} ({saved.Describe()}) for buyer {buyerId}.");
    }
}
