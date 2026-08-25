using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPalGateway;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, IPayPalGateway payPalGateway)
    {
        _repository = repository;
        _payPalGateway = payPalGateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _payPalGateway.CreatePaymentTokenAsync(card, buyerId, ct);

        var savedCard = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CardBrand ?? "Unknown", vaulted.Last4 ?? "????", vaulted.Expiry ?? card.Expiry);
        return await _repository.AddAsync(savedCard, ct);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var method = await _repository.GetByIdAsync(paymentMethodId, ct);
        if (method is null || method.BuyerId != buyerId)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        await _payPalGateway.DeletePaymentTokenAsync(method.PayPalVaultId, ct);
        await _repository.DeleteAsync(method, ct);
    }
}
