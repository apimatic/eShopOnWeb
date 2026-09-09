using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(IRepository<PaymentMethod> repository, IPayPalClient payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _payPal.VaultCardAsync(card, customerId: null,
            idempotencyKey: Guid.NewGuid().ToString("N"), ct);

        var paymentMethod = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.Brand, vaulted.LastFourDigits, vaulted.Expiry, vaulted.Name ?? card.Name);
        await _repository.AddAsync(paymentMethod, ct);

        _logger.LogInformation("Saved {0} card ending {1} for {2} (vault {3}).",
            vaulted.Brand, vaulted.LastFourDigits, buyerId, vaulted.VaultId);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var paymentMethod = await _repository.FirstOrDefaultAsync(
            new PaymentMethodByIdForBuyerSpecification(paymentMethodId, buyerId), ct);
        if (paymentMethod is null)
        {
            return false;
        }

        // Remove the card from PayPal's vault so it can no longer be charged, then drop our record.
        await _payPal.DeleteVaultedCardAsync(paymentMethod.VaultId, ct);
        await _repository.DeleteAsync(paymentMethod, ct);

        _logger.LogInformation("Deleted saved card {0} for {1}.", paymentMethodId, buyerId);
        return true;
    }
}
