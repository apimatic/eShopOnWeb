using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _paymentMethods;
    private readonly IPayPalPaymentService _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(IRepository<PaymentMethod> paymentMethods, IPayPalPaymentService payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _paymentMethods = paymentMethods;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // The card is vaulted at PayPal; only the token id and a safe descriptor come back and are stored.
        var vaulted = await _payPal.VaultCardAsync(card, customerId: null, cancellationToken);

        var paymentMethod = new PaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId,
            vaulted.CardBrand,
            vaulted.CardLastFour,
            vaulted.Expiry,
            vaulted.CardHolderName ?? card.Name);

        await _paymentMethods.AddAsync(paymentMethod, cancellationToken);
        _logger.LogInformation("Saved card {Description} (vault {VaultId}) for {BuyerId}",
            paymentMethod.Describe(), vaulted.VaultId, buyerId);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new PaymentMethodsByBuyerSpecification(buyerId);
        return await _paymentMethods.ListAsync(spec, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        // Scope the lookup to the caller so one shopper can never delete another's saved card.
        var spec = new PaymentMethodsByBuyerSpecification(buyerId, paymentMethodId);
        var paymentMethod = await _paymentMethods.FirstOrDefaultAsync(spec, cancellationToken);
        if (paymentMethod is null)
            return false;

        // Remove from the PayPal vault first so it can no longer be charged, then drop our record.
        await _payPal.DeleteVaultedCardAsync(paymentMethod.PayPalVaultId, cancellationToken);
        await _paymentMethods.DeleteAsync(paymentMethod, cancellationToken);
        _logger.LogInformation("Deleted saved card {Id} (vault {VaultId}) for {BuyerId}",
            paymentMethodId, paymentMethod.PayPalVaultId, buyerId);
        return true;
    }
}
