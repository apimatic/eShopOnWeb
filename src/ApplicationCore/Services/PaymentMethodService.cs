using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

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

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardInput card,
        CancellationToken cancellationToken = default)
    {
        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.LastFourDigits,
            vaulted.ExpiryYearMonth ?? card.ExpiryYearMonth,
            vaulted.CardholderName ?? card.CardholderName);

        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved card {0} ({1} ****{2}) for {3}.",
            saved.Id, saved.CardBrand, saved.LastFourDigits, buyerId);
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
            new SavedPaymentMethodByIdForBuyerSpecification(paymentMethodId, buyerId), cancellationToken)
            ?? throw new EntityNotFoundException($"Saved card {paymentMethodId} was not found.");

        // Remove from PayPal's vault first so it can never be charged again, then locally.
        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation("Deleted saved card {0} for {1}.", paymentMethodId, buyerId);
    }
}
