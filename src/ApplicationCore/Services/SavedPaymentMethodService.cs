using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _paymentGateway;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentSource card, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required to save a card.", 401);
        }

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var paypalCustomerId = existing.Select(p => p.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        var vaulted = await _paymentGateway.VaultCardAsync(
            card,
            merchantCustomerId: SanitizeMerchantCustomerId(buyerId),
            paypalCustomerId: paypalCustomerId,
            idempotencyKey: $"vault-{buyerId}-{System.Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId ?? paypalCustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return methods;
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await GetOwnedAsync(buyerId, paymentMethodId, cancellationToken);
        if (saved == null)
        {
            throw new PaymentException("The saved card was not found or is not available to this shopper.", 404);
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already removed at PayPal; still drop the local record so it cannot be reused.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }

    public Task<SavedPaymentMethod?> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        return _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId),
            cancellationToken);
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var sanitized = new string(buyerId.Where(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '^' or '*' or '$' or '@' or '#').ToArray());
        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64];
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "shopper" : sanitized;
    }
}
