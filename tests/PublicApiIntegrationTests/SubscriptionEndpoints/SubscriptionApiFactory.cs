using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

internal sealed class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    internal FakeMaxioBillingGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMaxioBillingGateway>();
            services.AddSingleton<IMaxioBillingGateway>(Gateway);
        });
    }
}

internal sealed class FakeMaxioBillingGateway : IMaxioBillingGateway
{
    private readonly ConcurrentDictionary<string, MaxioCustomer> _customers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MaxioSubscription> _subscriptions = new(StringComparer.Ordinal);
    private int _customerSequence = 100;
    private int _subscriptionSequence = 200;
    private int _createCustomerCalls;
    private int _createSubscriptionCalls;

    internal int CreateCustomerCalls => Volatile.Read(ref _createCustomerCalls);
    internal int CreateSubscriptionCalls => Volatile.Read(ref _createSubscriptionCalls);

    public Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioPlan> plans = new[]
        {
            new MaxioPlan("pro-plan", "Pro Plan", "For growing teams", 29900, 1, "month"),
            new MaxioPlan("basic-plan", "Basic Plan", "For individuals", 2900, 1, "month")
        };
        return Task.FromResult(plans);
    }

    public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        _customers.TryGetValue(reference, out var customer);
        return Task.FromResult(customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerProfile profile,
        string reference,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createCustomerCalls);
        await Task.Delay(50, cancellationToken);
        return _customers.GetOrAdd(reference, key =>
            new MaxioCustomer(Interlocked.Increment(ref _customerSequence), key));
    }

    public Task<MaxioSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        _subscriptions.TryGetValue(reference, out var subscription);
        return Task.FromResult(subscription);
    }

    public async Task<MaxioSubscriptionCreateResult> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createSubscriptionCalls);
        await Task.Delay(100, cancellationToken);
        var id = Interlocked.Increment(ref _subscriptionSequence);
        var candidate = new MaxioSubscription(
            id,
            subscriptionReference,
            productHandle,
            productHandle == "pro-plan" ? "Pro Plan" : "Basic Plan",
            productHandle == "pro-plan" ? 29900 : 2900,
            productHandle == "pro-plan" ? 29900 : 2900,
            "active",
            DateTimeOffset.UtcNow.AddMonths(1));
        var actual = _subscriptions.GetOrAdd(subscriptionReference, candidate);
        return new MaxioSubscriptionCreateResult(actual, ReferenceEquals(actual, candidate));
    }

    public Task<MaxioSubscription> ReadSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_subscriptions.Values.Single(subscription => subscription.Id == subscriptionId));
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioSubscription> subscriptions = _subscriptions.Values.ToList();
        return Task.FromResult(subscriptions);
    }
}
