using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentService _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentService payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var vaulted = await _payPal.VaultCardAsync(card, ct);
        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CardBrand,
            vaulted.LastFourDigits, vaulted.Expiry, vaulted.CardholderName);
        await _repository.AddAsync(saved, ct);

        _logger.LogInformation($"Saved card {saved.Id} ({vaulted.CardBrand} ending {vaulted.LastFourDigits}) for a shopper.");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped by owner: another shopper's card is indistinguishable from a missing one.
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodsByBuyerSpecification(buyerId, paymentMethodId), ct)
            ?? throw new EntityNotFoundException($"Saved card {paymentMethodId} was not found.");

        await _payPal.DeleteVaultedCardAsync(saved.VaultId, ct);
        await _repository.DeleteAsync(saved, ct);

        _logger.LogInformation($"Deleted saved card {paymentMethodId} for a shopper.");
    }
}
