using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saved-card operations. A card is vaulted with PayPal; this app stores only the vault token
/// and a safe description, always scoped to the shopper who saved it.
/// </summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(card, Guid.NewGuid().ToString("N"), cancellationToken);

        var method = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CardBrand,
            vaulted.LastDigits, vaulted.ExpiryYearMonth, vaulted.CardholderName);
        await _repository.AddAsync(method, cancellationToken);

        _logger.LogInformation($"Saved card {method.Id} ({vaulted.CardBrand} ****{vaulted.LastDigits}) for {buyerId}.");
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(paymentMethodId), cancellationToken);
        // A shopper can only delete their own card; another's (or a missing one) is reported the same way.
        if (method is null || !string.Equals(method.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentNotFoundException($"No saved card {paymentMethodId} was found for the current user.");
        }

        // Remove from PayPal first so the token can no longer be charged; then drop the local record.
        await _gateway.DeleteVaultedCardAsync(method.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(method, cancellationToken);

        _logger.LogInformation($"Deleted saved card {paymentMethodId} for {buyerId}.");
    }
}
