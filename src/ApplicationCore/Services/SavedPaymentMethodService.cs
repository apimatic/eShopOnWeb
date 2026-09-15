using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardInput card, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var requestId = $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}";
        var vaulted = await _payPal.VaultCardAsync(buyerId, card, requestId, cancellationToken);

        var entity = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.PayPalCustomerId,
            vaulted.LastDigits ?? "****",
            vaulted.Brand ?? "UNKNOWN",
            vaulted.Expiry,
            vaulted.CardholderName);

        await _repository.AddAsync(entity);
        return entity;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId));
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var method = await GetOwnedAsync(buyerId, paymentMethodId);
        await _payPal.DeleteVaultedCardAsync(method.PayPalPaymentTokenId, cancellationToken);
        await _repository.DeleteAsync(method);
    }

    public async Task<SavedPaymentMethod> GetOwnedAsync(string buyerId, int paymentMethodId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId));
        if (method is null)
        {
            throw new CheckoutException(404, "Payment method not found.");
        }

        return method;
    }
}
