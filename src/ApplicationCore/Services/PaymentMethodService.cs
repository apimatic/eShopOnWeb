using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves and manages a shopper's cards. The card is vaulted with PayPal; this app keeps only the vault
/// token plus safe display metadata. Every operation is scoped to the owning shopper.
/// </summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<CustomerPaymentMethod> _repository;
    private readonly IPayPalPaymentGateway _gateway;

    public PaymentMethodService(IRepository<CustomerPaymentMethod> repository, IPayPalPaymentGateway gateway)
    {
        _repository = repository;
        _gateway = gateway;
    }

    public async Task<CustomerPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vault = await _gateway.VaultCardAsync(card, cancellationToken);

        var method = new CustomerPaymentMethod(buyerId, vault.VaultId, vault.CardBrand, vault.Last4,
            vault.ExpiryMonth, vault.ExpiryYear, alias);

        return await _repository.AddAsync(method, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerPaymentMethod>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var methods = await _repository.ListAsync(new CustomerPaymentMethodsSpecification(buyerId), cancellationToken);
        return methods;
    }

    public async Task<bool> DeleteAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken = default)
    {
        var method = await _repository.FirstOrDefaultAsync(
            new CustomerPaymentMethodByIdSpecification(paymentMethodId, buyerId), cancellationToken);

        if (method is null)
        {
            return false;
        }

        await _repository.DeleteAsync(method, cancellationToken);
        return true;
    }
}
