using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken);
}

public sealed record MaxioPlan(int Id, string Handle, string Name, int PriceInCents, int Interval, string IntervalUnit, bool IsArchived);
public sealed record MaxioCustomer(int Id, string Reference);
public sealed record MaxioCustomerCreate(string FirstName, string LastName, string Email, string Reference);
public sealed record MaxioSubscription(
    int Id,
    int CustomerId,
    string State,
    string? ProductHandle,
    string? ProductName,
    int? ProductPriceInCents,
    int? ProductInterval,
    string? ProductIntervalUnit,
    System.DateTimeOffset? NextBillingAt,
    System.DateTimeOffset? CurrentPeriodEndsAt);
