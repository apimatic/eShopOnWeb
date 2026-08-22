using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _paymentGateway;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentInput card, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId, includeDeleted: true), cancellationToken);
        var paypalCustomerId = existing
            .Select(m => m.PaypalCustomerId)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var saved = await _paymentGateway.SaveCardAsync(
            new SaveCardRequest(
                buyerId,
                paypalCustomerId,
                $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
                card),
            cancellationToken);

        if (string.IsNullOrEmpty(saved.PaymentTokenId))
        {
            throw new PaymentGatewayException("PayPal did not return a payment token id.", 502);
        }

        var method = new SavedPaymentMethod(
            buyerId,
            saved.PaymentTokenId,
            saved.PaypalCustomerId ?? paypalCustomerId,
            saved.LastDigits,
            saved.Brand,
            saved.Expiry);

        return await _repository.AddAsync(method, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return methods.OrderByDescending(m => m.CreatedAt).ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var method = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (method is null || !string.Equals(method.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderPaymentException("The saved payment method was not found.", 404);
        }

        if (method.IsDeleted)
        {
            return;
        }

        try
        {
            await _paymentGateway.DeleteCardAsync(method.PaypalPaymentTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode is 404 or 400)
        {
            // Already removed at PayPal — still drop the local row.
        }

        method.MarkDeleted();
        await _repository.UpdateAsync(method, cancellationToken);
    }
}
