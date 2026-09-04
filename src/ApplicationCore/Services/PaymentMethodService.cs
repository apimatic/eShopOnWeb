using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _gateway;

    public PaymentMethodService(IRepository<PaymentMethod> paymentMethodRepository, IPayPalGateway gateway)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, GatewayCard card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Deterministic within a short window so a double-click replays the same vault
        // request, while two deliberate saves get distinct PayPal request ids.
        var idempotencyKey = $"eshop-vault-{buyerId}-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var result = await _gateway.SaveCardAsync(card, merchantCustomerId: buyerId, idempotencyKey, ct);

        if (!result.Success || string.IsNullOrEmpty(result.VaultId))
        {
            throw new PaymentDeclinedException(result.DeclineReason ?? "The card issuer declined saving this card.");
        }

        var paymentMethod = new PaymentMethod(buyerId, result.VaultId!, result.Brand, result.LastDigits, result.Expiry);
        return await _paymentMethodRepository.AddAsync(paymentMethod, ct);
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _paymentMethodRepository.ListAsync(new BuyerPaymentMethodsSpecification(buyerId), ct);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var paymentMethod = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, ct);
        if (paymentMethod is null || paymentMethod.BuyerId != buyerId)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        var result = await _gateway.DeleteCardAsync(paymentMethod.PayPalVaultId, ct);
        if (!result.Success)
        {
            throw new PayPalGatewayException(
                $"PayPal could not delete the saved card: {result.DeclineReason ?? "unknown reason"}");
        }

        await _paymentMethodRepository.DeleteAsync(paymentMethod, ct);
    }
}
