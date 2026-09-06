using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using CustomerSubscription = Microsoft.eShopWeb.ApplicationCore.Subscriptions.CustomerSubscription;
using SubscribeRequest = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscribeRequest;
using SubscribeResult = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscribeResult;
using SubscriberIdentity = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscriberIdentity;
using SubscriptionPlan = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscriptionPlan;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Stands in for <see cref="MaxioSubscriptionService"/> when the <c>Maxio:</c> section is missing
/// or incomplete.
/// <para>
/// Subscriptions are an additive capability alongside the existing storefront, so an absent
/// billing configuration degrades that one capability instead of stopping the host from starting -
/// which also keeps the app runnable for anyone who checks the repository out without credentials.
/// The problems are reported at startup and again on every attempted use.
/// </para>
/// </summary>
public sealed class UnconfiguredSubscriptionService : ISubscriptionService
{
    private readonly IReadOnlyCollection<string> _problems;
    private readonly ILogger<UnconfiguredSubscriptionService> _logger;

    public UnconfiguredSubscriptionService(IReadOnlyCollection<string> problems, ILogger<UnconfiguredSubscriptionService> logger)
    {
        _problems = problems;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        throw Fail();

    public Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default) =>
        throw Fail();

    public Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default) =>
        throw Fail();

    private BillingConfigurationException Fail()
    {
        var detail = string.Join(" ", _problems);
        _logger.LogError("A subscription endpoint was called but Maxio billing is not configured. {Problems}", detail);
        return new BillingConfigurationException("Subscription billing is not configured on this server. " + detail);
    }
}
