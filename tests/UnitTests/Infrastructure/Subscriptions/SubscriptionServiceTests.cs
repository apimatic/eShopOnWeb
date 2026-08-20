using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Subscriptions;

public class SubscriptionServiceTests
{
    [Fact]
    public async Task ConcurrentDoubleClickCreatesOneProviderSubscription()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var gateway = new FakeGateway();
        var operationLock = new SubscriptionOperationLock();

        await using var firstContext = new CatalogContext(options);
        await using var secondContext = new CatalogContext(options);
        var firstService = new SubscriptionService(firstContext, gateway, operationLock);
        var secondService = new SubscriptionService(secondContext, gateway, operationLock);

        var results = await Task.WhenAll(
            firstService.SubscribeAsync("user-1", "demo.user", "demo@example.test", "eshop-pro", default),
            secondService.SubscribeAsync("user-1", "demo.user", "demo@example.test", "eshop-pro", default));

        Assert.Equal(results[0].Reference, results[1].Reference);
        Assert.Equal(1, gateway.CreateCalls);
        await using var verificationContext = new CatalogContext(options);
        Assert.Single(await verificationContext.SubscriptionRecords.ToListAsync());
    }

    [Fact]
    public async Task FreshLocalClaimReconcilesExistingProviderSubscriptionBeforeCreate()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var gateway = new FakeGateway();
        var expectedReference = CreateReference("subscription", "user-1", "ESHOP-PRO");
        var expected = new SubscriptionDetails(
            expectedReference,
            "eshop-pro",
            "Pro",
            29900,
            "USD",
            "active",
            DateTimeOffset.UtcNow.AddMonths(1));
        gateway.Seed(expected);

        await using var context = new CatalogContext(options);
        var service = new SubscriptionService(context, gateway, new SubscriptionOperationLock());

        var result = await service.SubscribeAsync(
            "user-1",
            "demo.user",
            "demo@example.test",
            "eshop-pro",
            default);

        Assert.Equal(expected.Reference, result.Reference);
        Assert.Equal(0, gateway.CreateCalls);
        var record = Assert.Single(await context.SubscriptionRecords.ToListAsync());
        Assert.Equal(SubscriptionRecordStatus.Succeeded, record.Status);
    }

    private sealed class FakeGateway : ISubscriptionBillingGateway
    {
        private readonly ConcurrentDictionary<string, SubscriptionDetails> _subscriptions = new();
        private int _createCalls;

        public int CreateCalls => _createCalls;

        public void Seed(SubscriptionDetails subscription) =>
            _subscriptions[subscription.Reference] = subscription;

        public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>([Plan]);

        public Task<SubscriptionPlan?> GetPlanAsync(string productHandle, CancellationToken cancellationToken) =>
            Task.FromResult<SubscriptionPlan?>(
                string.Equals(productHandle, Plan.Handle, StringComparison.Ordinal) ? Plan : null);

        public Task EnsureCustomerAsync(SubscriptionCustomer customer, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<SubscriptionDetails?> FindSubscriptionAsync(
            string reference,
            CancellationToken cancellationToken) =>
            Task.FromResult(_subscriptions.TryGetValue(reference, out var result) ? result : null);

        public async Task<SubscriptionDetails> CreateSubscriptionAsync(
            string productHandle,
            string customerReference,
            string subscriptionReference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCalls);
            await Task.Delay(50, cancellationToken);
            var result = new SubscriptionDetails(
                subscriptionReference,
                productHandle,
                "Pro",
                29900,
                "USD",
                "active",
                DateTimeOffset.UtcNow.AddMonths(1));
            _subscriptions[subscriptionReference] = result;
            return result;
        }

        private static readonly SubscriptionPlan Plan =
            new("eshop-pro", "Pro", null, 29900, 1, "month");
    }

    private static string CreateReference(string kind, params string[] values)
    {
        var material = string.Join("\n", values);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return string.Concat("eshop-", kind, "-", hash.AsSpan(0, 32));
    }
}
