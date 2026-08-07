using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. All operations are scoped to a single owner so one shopper can
/// never see, use, or delete another's card.
/// </summary>
public interface IPaymentMethodService
{
    Task<SaveCardResult> SaveCardAsync(string ownerId, CardDetails card, string? alias, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> ListForOwnerAsync(string ownerId, CancellationToken cancellationToken = default);

    Task<DeleteCardResult> DeleteAsync(string ownerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

public enum SaveCardOutcome
{
    Saved,
    GatewayError
}

public sealed record SaveCardResult(SaveCardOutcome Outcome, PaymentMethod? PaymentMethod = null, string? Error = null);

public enum DeleteCardOutcome
{
    Deleted,
    NotFound
}

public sealed record DeleteCardResult(DeleteCardOutcome Outcome);
