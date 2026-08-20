using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private static readonly Regex NonCustomerIdChars = new("[^A-Za-z0-9_-]", RegexOptions.Compiled);

    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("A signed-in shopper is required to save a card.");
        }

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var paypalCustomerId = existing.Find(m => !string.IsNullOrEmpty(m.PayPalCustomerId))?.PayPalCustomerId;
        var merchantCustomerId = ToMerchantCustomerId(buyerId);
        var requestId = $"eshop-vault-{merchantCustomerId}-{System.Guid.NewGuid():N}";

        var vaulted = await _payPal.VaultCardAsync(
            card,
            merchantCustomerId,
            paypalCustomerId,
            requestId,
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.PayPalCustomerId ?? paypalCustomerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.Name);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpec(paymentMethodId),
            cancellationToken);

        if (saved is null)
        {
            throw new PaymentNotFoundException("Saved payment method was not found.");
        }

        if (!string.Equals(saved.BuyerId, buyerId, System.StringComparison.Ordinal))
        {
            throw new PaymentForbiddenException("You cannot delete another shopper's saved card.");
        }

        await _payPal.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
    }

    internal static string ToMerchantCustomerId(string buyerId)
    {
        var sanitized = NonCustomerIdChars.Replace(buyerId, "_");
        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64];
        }

        return string.IsNullOrEmpty(sanitized) ? "shopper" : sanitized;
    }
}
