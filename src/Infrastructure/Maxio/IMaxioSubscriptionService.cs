using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioUserInfo
{
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class SubscriptionEnrollment
{
    public MaxioCustomer Customer { get; set; } = new();
    public MaxioProduct Plan { get; set; } = new();
    public MaxioSubscription Subscription { get; set; } = new();
    public bool Created { get; set; }
}

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionEnrollment> EnrollAsync(MaxioUserInfo user, string? productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(MaxioUserInfo user, CancellationToken cancellationToken = default);
}

public class MaxioPlanNotFoundException : MaxioApiException
{
    public MaxioPlanNotFoundException(string productHandle)
        : base(404, new List<string> { $"Subscription plan '{productHandle}' was not found in the configured product family." })
    {
    }
}
