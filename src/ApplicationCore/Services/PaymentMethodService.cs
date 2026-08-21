using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPaymentGateway _paymentGateway;

    public PaymentMethodService(IRepository<PaymentMethod> repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(
        string buyerId,
        CardDetails card,
        string? alias,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _paymentGateway.SaveCardAsync(
            buyerId,
            card,
            Guid.NewGuid().ToString("N"),
            cancellationToken);

        var method = new PaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            alias ?? vaulted.Name);

        return await _repository.AddAsync(method, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new PaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var method = await _repository.FirstOrDefaultAsync(
            new PaymentMethodByIdForBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (method is null)
        {
            throw new PaymentException(404, "Saved payment method was not found.");
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(method.CardId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already removed at PayPal — still drop the local record.
        }

        await _repository.DeleteAsync(method, cancellationToken);
    }
}
