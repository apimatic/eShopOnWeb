using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states that no longer occupy a "slot" for their plan, so subscribing again
    // to the same plan should create a fresh subscription rather than be treated as a repeat.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioClient maxioClient, IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
    }

    public Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
        => _maxioClient.ListPlansAsync(_options.ProductFamilyHandle, cancellationToken);

    public async Task<MaxioSubscription> SubscribeAsync(string username, string planHandle, CancellationToken cancellationToken = default)
    {
        // eShopOnWeb's ASP.NET Identity accounts always have UserName == Email (see
        // AppIdentityDbContextSeed / Register.cshtml.cs), and the JWT only carries the
        // username, so it doubles as both the Maxio customer reference and email.
        var (firstName, lastName) = SplitDisplayName(username);
        var customer = await _maxioClient.EnsureCustomerAsync(
            reference: username,
            email: username,
            firstName: firstName,
            lastName: lastName,
            cancellationToken);

        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var live = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State));

        if (live is not null)
        {
            return live;
        }

        return await _maxioClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(username, cancellationToken);
        return customer is null
            ? Array.Empty<MaxioSubscription>()
            : await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private static (string FirstName, string LastName) SplitDisplayName(string username)
    {
        var localPart = username.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart)
            ? ("eShopOnWeb", "Customer")
            : (localPart, "Customer");
    }
}
