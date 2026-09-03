using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly ILogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalPaymentGateway gateway,
        ILogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        // Reuse the PayPal customer id already established for this shopper so all their cards group together.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        var customerId = existing.FirstOrDefault()?.PayPalCustomerId ?? string.Empty;

        var result = await _gateway.VaultCardAsync(new VaultCardCommand(customerId, card), ct);

        var method = new SavedPaymentMethod(
            buyerId,
            result.PayPalCustomerId,
            result.VaultTokenId,
            result.CardBrand,
            result.LastDigits,
            result.Expiry,
            result.CardholderName);

        method = await _repository.AddAsync(method, ct);
        _logger.LogInformation("Saved card {PaymentMethodId} for {BuyerId} (brand {Brand}, ****{Last4}).",
            method.Id, buyerId, method.CardBrand, method.LastDigits);
        return ToView(method);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken ct = default)
    {
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        return methods.Select(ToView).ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var method = (await _repository.ListAsync(new SavedPaymentMethodByIdSpec(paymentMethodId, buyerId), ct)).FirstOrDefault();
        if (method is null)
            return false;

        try
        {
            await _gateway.DeleteVaultedCardAsync(method.VaultTokenId, ct);
        }
        catch (PayPalGatewayException ex)
        {
            // Remove locally regardless so the card can no longer appear or be used to pay; log the mismatch.
            _logger.LogWarning(ex, "PayPal vault delete failed for token {TokenId}; removing local record anyway.", method.VaultTokenId);
        }

        await _repository.DeleteAsync(method, ct);
        _logger.LogInformation("Deleted saved card {PaymentMethodId} for {BuyerId}.", paymentMethodId, buyerId);
        return true;
    }

    private static SavedCardView ToView(SavedPaymentMethod m) =>
        new(m.Id, m.CardBrand, m.LastDigits, m.Expiry, m.CardholderName, m.CreatedAt);
}
