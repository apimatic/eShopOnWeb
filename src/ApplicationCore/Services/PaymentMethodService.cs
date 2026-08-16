using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalClient payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        // Reuse the shopper's existing PayPal customer id so all their cards live under one customer.
        var existing = await _repository.FirstOrDefaultAsync(new LatestSavedPaymentMethodByBuyerSpecification(buyerId), cancellationToken);
        var customerId = existing?.PayPalCustomerId;

        var requestId = Guid.NewGuid().ToString("N");
        var vaulted = await _payPal.VaultCardAsync(card, customerId, requestId, cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        saved = await _repository.AddAsync(saved, cancellationToken);

        _logger.LogInformation("Saved {0} card ending {1} for {2}.", saved.Brand, saved.LastFourDigits, buyerId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);

        // One shopper must never delete another's card; hide existence of cards that aren't theirs.
        if (saved is null || saved.BuyerId != buyerId)
        {
            throw new NotFoundException($"Saved card {paymentMethodId} was not found.");
        }

        // Remove from PayPal's vault first so it can no longer be used to pay, then from our store.
        await _payPal.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);

        _logger.LogInformation("Deleted saved card {0} for {1}.", paymentMethodId, buyerId);
    }
}
