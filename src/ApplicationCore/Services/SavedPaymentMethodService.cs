using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPalClient;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalClient payPalClient)
    {
        _repository = repository;
        _payPalClient = payPalClient;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException("Card number, expiry (YYYY-MM) and security code are required.");
        }

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId, includeDeleted: true), cancellationToken);
        var customerId = existing.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.PayPalCustomerId))?.PayPalCustomerId
                         ?? PaymentFormatting.ToPayPalCustomerId(buyerId);

        var vaulted = await _payPalClient.VaultCardAsync(
            card,
            customerId,
            $"vault-{customerId}-{existing.Count}",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(vaulted.VaultId))
        {
            throw new PaymentException("PayPal did not return a vault identifier for the saved card.");
        }

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            string.IsNullOrWhiteSpace(vaulted.CustomerId) ? customerId : vaulted.CustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(paymentMethodId), cancellationToken);
        if (saved == null || saved.IsDeleted || saved.BuyerId != buyerId)
        {
            throw new PaymentException("Saved payment method was not found.", HttpStatusCode.NotFound);
        }

        try
        {
            await _payPalClient.DeletePaymentTokenAsync(saved.PayPalVaultId, cancellationToken);
        }
        catch (PaymentException)
        {
            // Continue so a card PayPal already removed cannot linger in our list.
        }

        saved.MarkDeleted();
        await _repository.UpdateAsync(saved, cancellationToken);
    }
}
