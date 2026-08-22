using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPalGateway;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPalGateway)
    {
        _repository = repository;
        _payPalGateway = payPalGateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        var vaulted = await _payPalGateway.VaultCardAsync(
            card,
            SanitizeMerchantCustomerId(buyerId),
            $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.Name);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var items = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return items;
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId),
            cancellationToken);
        if (saved is null)
        {
            throw new PaymentException(404, "The saved card was not found, or it does not belong to the caller.");
        }

        try
        {
            await _payPalGateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already gone on PayPal; still remove the local record.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var cleaned = new char[buyerId.Length];
        var n = 0;
        foreach (var c in buyerId)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '*' or '^' or '$' or '@' or '#')
            {
                cleaned[n++] = c;
            }
        }

        var value = new string(cleaned, 0, n);
        if (value.Length == 0)
        {
            value = "buyer";
        }

        return value.Length <= 64 ? value : value[..64];
    }
}
