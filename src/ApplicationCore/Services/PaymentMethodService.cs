using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalPaymentGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        // Vault the card at PayPal. The raw card never touches our database.
        var vault = await _gateway.VaultCardAsync(card, buyerId, cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, vault.VaultId, vault.CustomerId,
            vault.Brand, vault.Last4, vault.Expiry, vault.CardholderName ?? card.CardholderName);
        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken)
            ?? throw new PaymentMethodNotFoundException(paymentMethodId);

        // Remove from the PayPal vault first so the token can no longer be used to pay.
        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
    }

    public async Task<string> ResolveVaultIdAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken)
            ?? throw new PaymentMethodNotFoundException(paymentMethodId);
        return saved.PayPalVaultId;
    }
}
