using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves and manages a shopper's vaulted cards. The card lives in PayPal's vault; only the vault
/// token id and a safe descriptor are kept locally. All access is scoped to the owning shopper.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, IPaymentGateway gateway, IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Result<SavedPaymentMethod>> SaveCardAsync(string buyerId, GatewayCardDetails card, CancellationToken ct = default)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
            return Result<SavedPaymentMethod>.Invalid(ServiceResults.Validation("card", "Card number and expiry are required."));

        GatewayVaultResult vaulted;
        try
        {
            vaulted = await _gateway.VaultCardAsync(card, $"vault-{buyerId}-{System.Guid.NewGuid():N}", ct);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning("Vaulting card for {0} failed: {1}", buyerId, ex.Message);
            return Result<SavedPaymentMethod>.Error(ex.Message);
        }

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.PayPalCustomerId,
            vaulted.CardBrand, vaulted.CardLast4, vaulted.Expiry, vaulted.CardholderName ?? card.CardholderName);
        await _repository.AddAsync(saved, ct);

        _logger.LogInformation("Saved card {0} for {1} ({2} ****{3}).", saved.Id, buyerId, saved.CardBrand, saved.CardLast4);
        return Result<SavedPaymentMethod>.Success(saved);
    }

    public async Task<Result<IReadOnlyList<SavedPaymentMethod>>> ListCardsAsync(string buyerId, CancellationToken ct = default)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        return Result<IReadOnlyList<SavedPaymentMethod>>.Success(cards.ToList());
    }

    public async Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdForBuyerSpec(paymentMethodId, buyerId), ct);
        if (saved is null)
            return Result.NotFound();

        try
        {
            await _gateway.DeleteVaultedCardAsync(saved.VaultId, ct);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning("Deleting vaulted card {0} for {1} failed at PayPal: {2}", paymentMethodId, buyerId, ex.Message);
            return Result.Error(ex.Message);
        }

        await _repository.DeleteAsync(saved, ct);
        _logger.LogInformation("Deleted saved card {0} for {1}.", paymentMethodId, buyerId);
        return Result.Success();
    }
}
