using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway paymentGateway,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveAsync(
        string buyerId,
        string merchantCustomerId,
        CardDetails card,
        CancellationToken cancellationToken = default)
    {
        var vaulted = await _paymentGateway.VaultCardAsync(merchantCustomerId, card, $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}", cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry ?? card.Expiry,
            vaulted.CardholderName ?? card.Name);

        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved payment method {PaymentMethodId} for buyer {BuyerId}.", saved.Id, buyerId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            throw new PaymentException("Saved payment method was not found.", 404, "PAYMENT_METHOD_NOT_FOUND");
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("PayPal vault token {TokenId} was already absent.", saved.PayPalPaymentTokenId);
        }

        await _repository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation("Deleted payment method {PaymentMethodId} for buyer {BuyerId}.", paymentMethodId, buyerId);
    }
}
