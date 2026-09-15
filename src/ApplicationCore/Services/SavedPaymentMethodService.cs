using System;
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

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "A signed-in shopper is required to save a card.");
        }

        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentOperationException(400, "Card number and expiry (YYYY-MM) are required.");
        }

        var merchantCustomerId = SanitizeMerchantCustomerId(buyerId);
        var vaulted = await _payPal.VaultCardAsync(
            merchantCustomerId,
            card,
            Guid.NewGuid().ToString("N"),
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.Name ?? card.Name);

        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved payment method {PaymentMethodId} for buyer ending {LastDigits}.", saved.Id, saved.LastDigits ?? "xxxx");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return methods;
    }

    public async Task DeleteAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByBuyerAndIdSpec(buyerId, paymentMethodId),
            cancellationToken);
        if (method is null)
        {
            throw new PaymentOperationException(404, "Saved card was not found or does not belong to this shopper.");
        }

        await _payPal.DeleteVaultedCardAsync(method.PayPalPaymentTokenId, cancellationToken);
        await _repository.DeleteAsync(method, cancellationToken);
        _logger.LogInformation("Deleted payment method {PaymentMethodId}.", paymentMethodId);
    }

    internal static string SanitizeMerchantCustomerId(string buyerId)
    {
        var chars = buyerId.Where(ch => char.IsLetterOrDigit(ch) || "-_.^*$@#".Contains(ch)).ToArray();
        var sanitized = new string(chars);
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "buyer";
        }

        return sanitized.Length <= 64 ? sanitized : sanitized[..64];
    }
}
