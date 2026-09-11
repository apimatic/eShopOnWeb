using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentProcessor _processor;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentProcessor processor,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _processor = processor;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's existing PayPal vault customer id so all their cards group together.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        var customerId = existing.Select(m => m.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var result = await _processor.VaultCardAsync(new VaultCardRequest
        {
            Card = card,
            PayPalCustomerId = customerId
        }, ct);

        var saved = new SavedPaymentMethod(buyerId, result.VaultTokenId, result.PayPalCustomerId ?? customerId,
            result.Brand, result.LastDigits, result.Expiry, result.CardholderName);
        await _repository.AddAsync(saved, ct);
        _logger.LogInformation("Saved card for buyer: token={0} brand={1} last4={2}", result.VaultTokenId,
            result.Brand, result.LastDigits);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetForBuyerAsync(string buyerId,
        CancellationToken ct = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var saved = await _repository.GetByIdAsync(paymentMethodId, ct);
        if (saved is null || saved.BuyerId != buyerId)
            throw new PaymentNotFoundException("The specified saved card was not found.");

        await _processor.DeleteVaultedCardAsync(saved.VaultTokenId, ct);
        await _repository.DeleteAsync(saved, ct);
        _logger.LogInformation("Deleted saved card {0} (token {1}) for buyer.", paymentMethodId, saved.VaultTokenId);
    }
}
