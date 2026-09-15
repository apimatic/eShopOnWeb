using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodAppService : IPaymentMethodAppService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPayPalPaymentService _payPal;

    public PaymentMethodAppService(IRepository<PaymentMethod> repository, IPayPalPaymentService payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var idempotencyKey = Guid.NewGuid().ToString("N");
        var vaulted = await _payPal.VaultCardAsync(card, idempotencyKey, ct);

        var method = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.CardBrand, vaulted.LastFourDigits,
            card.CardholderName, vaulted.Expiry);

        return await _repository.AddAsync(method, ct);
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task<bool> DeleteAsync(int paymentMethodId, string buyerId, CancellationToken ct = default)
    {
        var method = await _repository.GetByIdAsync(paymentMethodId, ct);
        if (method is null || method.BuyerId != buyerId)
        {
            // One shopper must never delete another's card; treat as not found.
            return false;
        }

        await _payPal.DeleteVaultedCardAsync(method.PayPalVaultId, ct);
        await _repository.DeleteAsync(method, ct);
        return true;
    }
}
