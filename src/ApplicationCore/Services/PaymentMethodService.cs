using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
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

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _payPal.VaultCardAsync(card, Guid.NewGuid().ToString("N"), ct);

        var paymentMethod = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, alias);
        await _repository.AddAsync(paymentMethod, ct);

        _logger.LogInformation($"Saved card {vaulted.Brand} ****{vaulted.Last4} for {buyerId} (payment method {paymentMethod.Id}).");
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var pm = await _repository.FirstOrDefaultAsync(new PaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), ct)
            ?? throw new EntityNotFoundException($"Saved card {paymentMethodId} was not found.");

        await _payPal.DeleteVaultedCardAsync(pm.VaultId, ct);
        await _repository.DeleteAsync(pm, ct);

        _logger.LogInformation($"Deleted saved card {paymentMethodId} for {buyerId}.");
    }
}
