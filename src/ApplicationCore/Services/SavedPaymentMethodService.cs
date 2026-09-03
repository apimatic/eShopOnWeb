using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
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

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken)
    {
        var vaulted = await _paymentGateway.VaultCardAsync(SanitizeCustomerId(buyerId), card, cancellationToken);
        var entity = new SavedPaymentMethod(
            buyerId,
            vaulted.PayPalVaultId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName);
        return await _repository.AddAsync(entity, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var method = await GetOwnedAsync(buyerId, paymentMethodId, cancellationToken);
        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(method.PayPalVaultId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone at PayPal; still drop the local mapping.
        }

        await _repository.DeleteAsync(method, cancellationToken);
    }

    public async Task<SavedPaymentMethod> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var method = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(paymentMethodId), cancellationToken);
        if (method is null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        if (!string.Equals(method.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ForbiddenResourceException("This payment method does not belong to the caller.");
        }

        return method;
    }

    private static string SanitizeCustomerId(string buyerId)
    {
        var sanitized = new string(buyerId.Where(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '^' or '*' or '$' or '@' or '#').ToArray());
        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64];
        }

        return string.IsNullOrEmpty(sanitized) ? "buyer" : sanitized;
    }
}
