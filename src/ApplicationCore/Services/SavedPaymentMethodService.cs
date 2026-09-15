using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentsClient _payPal;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentsClient payPal,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveAsync(
        string buyerId,
        CardPaymentInput card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw OrderPaymentException.Forbidden("A signed-in shopper is required to save a card.");
        }

        var existing = await _repository.ListAsync(
            new SavedPaymentMethodsByBuyerSpecification(buyerId),
            cancellationToken);
        var customerId = existing.FirstOrDefault(m => !string.IsNullOrEmpty(m.PayPalCustomerId))?.PayPalCustomerId;

        var vaulted = await _payPal.VaultCardAsync(
            OrderPaymentService.ToCardRequest(card),
            customerId,
            $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId ?? customerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.Name);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw OrderPaymentException.Forbidden("A signed-in shopper is required.");
        }

        return await _repository.ListAsync(
            new SavedPaymentMethodsByBuyerSpecification(buyerId),
            cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId),
            cancellationToken);

        if (saved == null || saved.IsDeleted)
        {
            throw OrderPaymentException.NotFound("Saved payment method was not found.");
        }

        if (!saved.BelongsTo(buyerId))
        {
            throw OrderPaymentException.Forbidden("You cannot delete another shopper's saved card.");
        }

        try
        {
            await _payPal.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (OrderPaymentException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning(
                "PayPal payment token {TokenId} was already absent when deleting saved payment method {PaymentMethodId}.",
                saved.PayPalPaymentTokenId,
                saved.Id);
        }

        saved.MarkDeleted();
        await _repository.UpdateAsync(saved, cancellationToken);
    }
}
