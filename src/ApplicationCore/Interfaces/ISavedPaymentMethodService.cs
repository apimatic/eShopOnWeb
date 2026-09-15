using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPayment card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> GetOwnedAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default);
}

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(
        System.DateTimeOffset from,
        System.DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class ReconciliationReport
{
    public required System.DateTimeOffset From { get; init; }
    public required System.DateTimeOffset To { get; init; }
    public required IReadOnlyList<MatchedReconciliationRow> Matched { get; init; }
    public required IReadOnlyList<PayPalReportedTransaction> PaypalOnly { get; init; }
    public required IReadOnlyList<Order> EshopOnly { get; init; }
}

public sealed class MatchedReconciliationRow
{
    public required PayPalReportedTransaction PaypalTransaction { get; init; }
    public required Order Order { get; init; }
}
