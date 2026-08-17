using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPal,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentValidationException("Card number and expiry are required to save a card.");
        }

        // Unique per call: vaulting produces a one-time setup token, so a reused request id would
        // make PayPal replay an already-consumed token from an earlier run.
        var requestId = $"vault-{buyerId}-{System.Guid.NewGuid():N}";
        var vaulted = await _payPal.VaultCardAsync(card, requestId, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.Brand, vaulted.LastFour, vaulted.Expiry, vaulted.CardholderName);
        saved = await _repository.AddAsync(saved, cancellationToken);

        _logger.LogInformation("Saved a {0} card for {1} (vault {2}).", saved.Brand, buyerId, saved.VaultId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteCardAsync(
        string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            return false;
        }

        // Remove at PayPal first so it can no longer be used to pay, then locally.
        await _payPal.DeleteVaultedCardAsync(saved.VaultId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);

        _logger.LogInformation("Deleted saved card {0} for {1}.", paymentMethodId, buyerId);
        return true;
    }
}
