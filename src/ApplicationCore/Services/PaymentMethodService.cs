using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<PaymentMethod> repository,
        IPayPalGateway payPal,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // The card is vaulted at PayPal; only the vault id + a safe descriptor come back and are stored.
        var vaulted = await _payPal.VaultCardAsync(card, cancellationToken);
        var method = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry);
        await _repository.AddAsync(method, cancellationToken);
        _logger.LogInformation("Saved card for {0}: {1} ****{2}", buyerId, vaulted.Brand, vaulted.LastDigits);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new PaymentMethodByIdForBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (method is null)
            return false;

        await _repository.DeleteAsync(method, cancellationToken);
        _logger.LogInformation("Deleted saved card {0} for {1}", paymentMethodId, buyerId);
        return true;
    }
}
