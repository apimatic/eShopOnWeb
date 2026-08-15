using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _paymentMethods;
    private readonly IPayPalGateway _paypal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> paymentMethods,
        IPayPalGateway paypal,
        IAppLogger<PaymentMethodService> logger)
    {
        _paymentMethods = paymentMethods;
        _paypal = paypal;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        var requestId = $"vault-{Guid.NewGuid():N}";
        var vaulted = await _paypal.VaultCardAsync(card, requestId, ct);

        var method = new PaymentMethod(buyerId, vaulted.VaultTokenId, vaulted.Brand, vaulted.LastDigits,
            vaulted.Expiry, vaulted.CardholderName);
        method = await _paymentMethods.AddAsync(method, ct);

        _logger.LogInformation("Shopper {0} saved card {1} ({2}).", buyerId, method.Id, method.Display);
        return ToView(method);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId, CancellationToken ct = default)
    {
        var methods = await _paymentMethods.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), ct);
        return methods.Select(ToView).ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var method = await _paymentMethods.FirstOrDefaultAsync(
            new PaymentMethodByIdForBuyerSpecification(paymentMethodId, buyerId), ct);
        if (method is null)
        {
            // Not found and not-owned answer identically so one shopper cannot probe another's cards.
            throw PaymentApiException.NotFound($"Saved card {paymentMethodId} was not found for this shopper.");
        }

        try
        {
            await _paypal.DeleteVaultedCardAsync(method.VaultTokenId, ct);
        }
        catch (PayPalException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal — still remove locally so it is neither listed nor usable.
            _logger.LogWarning("Vault token for saved card {0} was already absent at PayPal.", paymentMethodId);
        }

        await _paymentMethods.DeleteAsync(method, ct);
        _logger.LogInformation("Shopper {0} removed saved card {1}.", buyerId, paymentMethodId);
    }

    private static SavedCardView ToView(PaymentMethod method) => new(
        method.Id, method.Brand, method.LastDigits, method.Expiry, method.CardholderName, method.CreatedAt, method.Display);
}
