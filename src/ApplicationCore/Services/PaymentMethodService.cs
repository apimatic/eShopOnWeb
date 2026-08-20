using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _paymentGateway;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<SavedPaymentMethod> SaveAsync(
        string buyerId,
        CardPaymentInput card,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var merchantCustomerId = SanitizeMerchantCustomerId(buyerId);
        var vaulted = await _paymentGateway.VaultCardAsync(
            merchantCustomerId,
            card,
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            cts.Token);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.PaypalCustomerId,
            vaulted.MerchantCustomerId ?? merchantCustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry);

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
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(saved.VaultId, cts.Token);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already removed at PayPal; drop the local record.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var sanitized = new char[Math.Min(buyerId.Length, 64)];
        var n = 0;
        foreach (var c in buyerId)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '^' or '*' or '$' or '@' or '#')
            {
                sanitized[n++] = c;
            }
            else if (c is '@' or '+' or ' ')
            {
                sanitized[n++] = '-';
            }
        }

        return n == 0 ? $"buyer-{buyerId.GetHashCode():X}" : new string(sanitized, 0, n);
    }
}
