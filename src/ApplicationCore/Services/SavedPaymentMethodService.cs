using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPalClient;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalClient payPalClient)
    {
        _repository = repository;
        _payPalClient = payPalClient;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var requestId = $"eshop-vault-{buyerId}-{System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var setupToken = await _payPalClient.CreateSetupTokenAsync(card, $"{requestId}-setup", cancellationToken);
        if (!string.Equals(setupToken.Status, "APPROVED", System.StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentVerificationRequiredException(
                $"PayPal could not vault the card without an additional shopper verification step (setup token status: {setupToken.Status}).");
        }

        var vaulted = await _payPalClient.CreatePaymentTokenAsync(setupToken.SetupTokenId, $"{requestId}-token", cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.CustomerId ?? setupToken.CustomerId ?? string.Empty,
            vaulted.VaultTokenId,
            vaulted.Brand,
            vaulted.LastFourDigits,
            vaulted.Expiry,
            card.CardholderName);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerIdSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var methods = await _repository.ListAsync(new SavedPaymentMethodsByBuyerIdSpecification(buyerId), cancellationToken);
        var saved = methods.FirstOrDefault(m => m.Id == savedPaymentMethodId);
        if (saved == null)
        {
            return false;
        }

        await _payPalClient.DeletePaymentTokenAsync(saved.VaultTokenId, cancellationToken);
        await _repository.DeleteAsync(saved, cancellationToken);
        return true;
    }
}
