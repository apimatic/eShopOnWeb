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

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPalClient;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPalClient,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _payPalClient = payPalClient;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's existing PayPal customer id so all their cards vault under one customer.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var customerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var vaulted = await _payPalClient.VaultCardAsync(card, customerId, Guid.NewGuid().ToString("N"), cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.TokenId,
            vaulted.CustomerId,
            vaulted.CardBrand,
            vaulted.CardLast4,
            vaulted.Expiry,
            label);

        saved = await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation($"Saved card {saved.Id} ({saved.CardBrand} ****{saved.CardLast4}) for buyer {buyerId}.");
        return saved;
    }

    public async Task<IReadOnlyCollection<SavedPaymentMethod>> GetForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved is null || saved.BuyerId != buyerId)
        {
            // Same response whether missing or someone else's, so ownership is never leaked.
            throw new ForbiddenAccessException($"Saved card {paymentMethodId} is not available to this shopper.");
        }

        // Revoke the token at PayPal first so it can no longer be used to pay. If PayPal no longer
        // knows it, that is fine — the end state (unusable) is what we want.
        try
        {
            await _payPalClient.DeleteVaultTokenAsync(saved.PayPalVaultId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning($"Vault token for saved card {paymentMethodId} was already gone at PayPal.");
        }

        await _repository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} for buyer {buyerId}.");
    }
}
