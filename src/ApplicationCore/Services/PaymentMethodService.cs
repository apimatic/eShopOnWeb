using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalClient payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Group all of this shopper's cards under one PayPal customer id.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        var customerId = existing.Select(c => c.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var result = await _payPal.VaultCardAsync(card, customerId, Guid.NewGuid().ToString("N"), ct);

        var saved = new SavedPaymentMethod(buyerId, result.VaultId, result.CustomerId ?? customerId,
            result.Brand, result.Last4, result.Expiry, result.CardholderName);
        saved = await _repository.AddAsync(saved, ct);

        _logger.LogInformation($"Shopper {buyerId} saved card {saved.Brand} ****{saved.Last4} (id {saved.Id}).");
        return ToView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        return cards.Select(ToView).ToList();
    }

    public async Task DeleteAsync(int paymentMethodId, string buyerId, CancellationToken ct = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpec(paymentMethodId, buyerId), ct);
        if (saved is null)
            throw new EntityNotFoundException($"Saved card {paymentMethodId} was not found.");

        try
        {
            await _payPal.DeleteVaultedCardAsync(saved.PayPalVaultId, ct);
        }
        catch (PaymentException ex)
        {
            // If PayPal no longer knows the token, still remove it locally so it can't be used to pay.
            _logger.LogWarning($"Deleting vaulted card {saved.PayPalVaultId} at PayPal reported: {ex.Message}");
        }

        await _repository.DeleteAsync(saved, ct);
        _logger.LogInformation($"Shopper {buyerId} removed saved card {paymentMethodId}.");
    }

    private static SavedCardView ToView(SavedPaymentMethod c) =>
        new(c.Id, c.Brand, c.Last4, c.Expiry, c.CardholderName, c.CreatedAt);
}
