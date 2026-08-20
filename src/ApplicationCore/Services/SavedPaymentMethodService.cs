using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedPaymentMethodService> _logger;

    public SavedPaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<SavedPaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default)
    {
        OrderPaymentService.ValidateCard(card);

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var paypalCustomerId = existing.Count > 0 ? existing[0].PayPalCustomerId : null;

        var vaulted = await _payPal.VaultCardAsync(card, paypalCustomerId, $"eshop-vault-{System.Guid.NewGuid():N}", cancellationToken);

        var method = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId ?? paypalCustomerId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.Name);

        await _repository.AddAsync(method, cancellationToken);
        _logger.LogInformation("Saved payment method {0} for buyer {1}.", method.Id, buyerId);
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByBuyerAndIdSpecification(buyerId, paymentMethodId), cancellationToken);
        if (method is null)
        {
            throw new PaymentException("Saved payment method was not found.", HttpStatusCode.NotFound);
        }

        await _payPal.DeleteVaultedCardAsync(method.PayPalPaymentTokenId, cancellationToken);
        await _repository.DeleteAsync(method, cancellationToken);
    }
}
