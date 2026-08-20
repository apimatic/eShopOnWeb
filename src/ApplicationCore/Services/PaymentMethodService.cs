using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default)
    {
        if (card is null)
        {
            throw new PaymentException(400, "Card details are required to save a payment method.");
        }

        var vaulted = await _payPal.VaultCardAsync(
            card,
            ToMerchantCustomerId(buyerId),
            $"eshop-vault-{ToMerchantCustomerId(buyerId)}-{System.Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            vaulted.Name);
        await _repository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved a PayPal payment token for buyer {0}.", buyerId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return methods;
    }

    public async Task DeleteAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId), cancellationToken);
        if (saved is null || saved.BuyerId != buyerId)
        {
            throw new PaymentException(404, "The saved payment method was not found.");
        }

        await _payPal.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation("Deleted saved payment method {0} for buyer {1}.", paymentMethodId, buyerId);
    }

    internal static string ToMerchantCustomerId(string buyerId)
    {
        var sanitized = Regex.Replace(buyerId, @"[^A-Za-z0-9_-]", "_");
        if (sanitized.Length == 0)
        {
            sanitized = "shopper";
        }

        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64];
        }

        return sanitized;
    }
}
