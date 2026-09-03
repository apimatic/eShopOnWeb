using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _methods;
    private readonly IPaymentGateway _payments;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> methods, IPaymentGateway payments)
    {
        _methods = methods;
        _payments = payments;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var vaulted = await _payments.VaultCardAsync(buyerId, card, requestId: null, ct);

        var method = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.PayPalCustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName);
        await _methods.AddAsync(method, ct);
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _methods.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var method = await _methods.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), ct);
        if (method is null)
            throw new PaymentException("Saved payment method was not found.", HttpStatusCode.NotFound);

        try
        {
            await _payments.DeleteVaultedCardAsync(method.PayPalVaultId, ct);
        }
        catch (PaymentException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone at PayPal — still remove locally.
        }

        method.MarkDeleted();
        await _methods.UpdateAsync(method, ct);
    }
}
