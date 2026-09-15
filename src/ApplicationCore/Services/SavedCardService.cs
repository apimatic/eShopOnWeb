using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Vaulting is a single logical operation for this buyer+card save request.
        var requestId = $"eshop-vault-{Guid.NewGuid():N}";
        var vaulted = await _payPal.VaultCardAsync(card, requestId, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, vaulted.CardholderName);
        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved card {0} ({1} ending {2}) for buyer {3}",
            saved.Id, saved.CardBrand, saved.Last4, buyerId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(
        string buyerId, CancellationToken cancellationToken = default)
        => await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);

    public async Task DeleteCardAsync(
        string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), cancellationToken)
            ?? throw new PaymentMethodNotFoundException(paymentMethodId);

        // Remove from PayPal's vault first so a deleted card can no longer be charged,
        // then drop the local record.
        await _payPal.DeleteVaultedCardAsync(saved.VaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation("Deleted saved card {0} for buyer {1}", paymentMethodId, buyerId);
    }
}
