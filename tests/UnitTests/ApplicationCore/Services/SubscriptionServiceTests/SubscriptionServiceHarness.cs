using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

/// <summary>
/// Shared doubles for the subscription service tests: a mocked Maxio client plus an
/// in-memory stand-in for the repository.
/// </summary>
public class SubscriptionServiceHarness
{
    public const string USER_ID = "demouser@microsoft.com";
    public const string PLAN_HANDLE = "eshop-pro";
    public const string FAMILY_HANDLE = "eshop-subscribe";

    public List<MaxioPlan> Plans { get; set; } = new List<MaxioPlan>
    {
        new MaxioPlan(1, "basic-plan", "Basic Plan", null, 2900, 1, "month", false, FAMILY_HANDLE, "eShopSubscribe", null),
        new MaxioPlan(2, PLAN_HANDLE, "Pro Plan", null, 29900, 1, "month", false, FAMILY_HANDLE, "eShopSubscribe", null)
    };

    public IMaxioAdvancedBillingClient Maxio { get; } = Substitute.For<IMaxioAdvancedBillingClient>();
    public IRepository<UserSubscription> Repository { get; } = Substitute.For<IRepository<UserSubscription>>();
    public IAppLogger<SubscriptionService> Logger { get; } = Substitute.For<IAppLogger<SubscriptionService>>();
    public List<UserSubscription> Store { get; } = new List<UserSubscription>();

    public MaxioCustomer Customer = new MaxioCustomer(555, $"eshoponweb-user:{USER_ID}", "demouser", "eShopOnWeb", USER_ID);

    public SubscriptionServiceHarness()
    {
        Maxio.GetPlansAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult((IReadOnlyList<MaxioPlan>)Plans.ToList()));

        Repository.ListAsync(Arg.Any<ISpecification<UserSubscription>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Store.ToList()));

        Repository.AddAsync(Arg.Any<UserSubscription>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var entity = call.Arg<UserSubscription>();
                Store.Add(entity);
                return Task.FromResult(entity);
            });

        Repository.UpdateAsync(Arg.Any<UserSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    public MaxioSubscription LiveSubscription(long? id = null, string? reference = null, string state = "active")
    {
        return new MaxioSubscription(
            id ?? 94685018,
            reference ?? $"eshoponweb-sub:{USER_ID}:{PLAN_HANDLE}",
            state,
            Customer.CustomerId,
            2,
            PLAN_HANDLE,
            "Pro Plan",
            29900,
            "USD",
            DateTimeOffset.UtcNow.AddMonths(1),
            DateTimeOffset.UtcNow.AddMonths(1),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            "remittance");
    }

    public SubscriptionService CreateService()
        => new SubscriptionService(
            Repository,
            Maxio,
            new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = FAMILY_HANDLE },
            Logger);
}
