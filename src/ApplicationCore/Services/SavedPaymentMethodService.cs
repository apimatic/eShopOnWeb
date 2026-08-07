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

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse this shopper's existing PayPal customer id so all their cards are
        // grouped under one customer; on the first save PayPal mints a new one.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var customerId = existing.FirstOrDefault()?.PayPalCustomerId;

        var vaulted = await _payPalClient.VaultCardAsync(card, customerId, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.Expiry,
            vaulted.CardholderName);

        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation($"Saved a {vaulted.Brand} card ending {vaulted.Last4} for a shopper.");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return methods;
    }

    public async Task DeleteAsync(string buyerId, int savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _repository.GetByIdAsync(savedPaymentMethodId, cancellationToken);

        // Missing or owned by another shopper -> treated identically, nothing disclosed.
        if (method is null || method.BuyerId != buyerId)
            throw new EntityNotFoundException($"Saved payment method {savedPaymentMethodId} was not found.");

        // Best-effort removal from PayPal's vault so the card can no longer be charged
        // anywhere. Even if this fails, removing our record makes it unusable via this API.
        try
        {
            await _payPalClient.DeleteVaultedCardAsync(method.PayPalVaultId, cancellationToken);
        }
        catch (PayPalException ex)
        {
            _logger.LogWarning($"PayPal vault deletion failed for a saved card; removing local record anyway. {ex.Message}");
        }

        await _repository.DeleteAsync(method, cancellationToken);
    }
}
