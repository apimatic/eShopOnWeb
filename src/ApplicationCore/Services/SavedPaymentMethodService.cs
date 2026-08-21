using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentsGateway _payPal;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentsGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentRequest card,
        CancellationToken cancellationToken = default)
    {
        var source = OrderCheckoutService.ToCardSource(card);
        var vaulted = await _payPal.VaultCardAsync(
            source,
            buyerId,
            $"eshop-vault-{Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry ?? source.Expiry ?? "0000-00");

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await GetOwnedAsync(buyerId, paymentMethodId, cancellationToken);
        await _payPal.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        saved.MarkDeleted();
        await _repository.UpdateAsync(saved, cancellationToken);
    }

    public async Task<SavedPaymentMethod> GetOwnedAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpec(paymentMethodId),
            cancellationToken);

        if (saved is null || saved.IsDeleted)
        {
            throw new EntityNotFoundException($"Payment method {paymentMethodId} was not found.");
        }

        if (!string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ForbiddenOperationException("You cannot use another shopper's saved card.");
        }

        return saved;
    }
}
