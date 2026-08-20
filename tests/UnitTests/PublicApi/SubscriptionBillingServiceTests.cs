using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

public class SubscriptionBillingServiceTests
{
    private static readonly BillingUser User = new("user-1", "shopper@example.test", "Shopper", "Test");

    [Fact]
    public async Task ConcurrentDoubleClickCreatesOneUpstreamSubscription()
    {
        var gateway = new FakeGateway();
        var keyLock = new SubscriptionKeyLock();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var firstContext = CreateContext(databaseRoot);
        await using var secondContext = CreateContext(databaseRoot);
        var firstService = CreateService(gateway, keyLock, firstContext);
        var secondService = CreateService(gateway, keyLock, secondContext);

        var results = await Task.WhenAll(
            firstService.SubscribeAsync(User, "eshop-pro", default),
            secondService.SubscribeAsync(User, "eshop-pro", default));

        Assert.Equal(1, gateway.CreateSubscriptionCalls);
        Assert.Single(results.Where(x => x.Created));
        Assert.All(results, x => Assert.False(x.IsUnknown));
        Assert.All(results, x => Assert.Equal(42, x.Subscription!.Id));
    }

    [Fact]
    public async Task UnknownOutcomeNeverAuthorizesAnotherCreate()
    {
        var gateway = new FakeGateway { CreateHasUnknownOutcome = true };
        var keyLock = new SubscriptionKeyLock();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var firstContext = CreateContext(databaseRoot);
        await using var secondContext = CreateContext(databaseRoot);
        var firstService = CreateService(gateway, keyLock, firstContext);
        var secondService = CreateService(gateway, keyLock, secondContext);

        var first = await firstService.SubscribeAsync(User, "eshop-pro", default);
        var second = await secondService.SubscribeAsync(User, "eshop-pro", default);

        Assert.True(first.IsUnknown);
        Assert.True(second.IsUnknown);
        Assert.Equal(1, gateway.CreateSubscriptionCalls);
    }

    private static CatalogContext CreateContext(InMemoryDatabaseRoot root)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("subscription-tests", root)
            .Options;
        return new CatalogContext(options);
    }

    private static SubscriptionBillingService CreateService(
        IMaxioBillingGateway gateway,
        SubscriptionKeyLock keyLock,
        CatalogContext context) =>
        new(
            gateway,
            context,
            keyLock,
            Options.Create(new MaxioOptions
            {
                ApiKey = "test",
                Subdomain = "test",
                ProductFamilyHandle = "eshop-subscribe"
            }));

    private sealed class FakeGateway : IMaxioBillingGateway
    {
        private SubscriptionDto? _subscription;
        public int CreateSubscriptionCalls { get; private set; }
        public bool CreateHasUnknownOutcome { get; init; }

        public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(Array.Empty<SubscriptionPlanDto>());

        public Task<MaxioProduct?> FindProductAsync(string productHandle, CancellationToken cancellationToken) =>
            Task.FromResult<MaxioProduct?>(new MaxioProduct(
                7,
                productHandle,
                "Pro",
                null,
                29900,
                1,
                IntervalUnit.Month.Value,
                "eshop-subscribe",
                false));

        public Task<MaxioCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken) =>
            Task.FromResult<MaxioCustomer?>(new MaxioCustomer(9, customerReference));

        public Task<MaxioCustomer> CreateCustomerAsync(
            BillingUser user,
            string customerReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MaxioCustomer(9, customerReference));

        public Task<NoCardPaymentCollectionMethod> ResolveNoCardPaymentCollectionMethodAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(NoCardPaymentCollectionMethod.Remittance);

        public Task<SubscriptionDto?> FindSubscriptionAsync(
            string subscriptionReference,
            CancellationToken cancellationToken) => Task.FromResult(_subscription);

        public async Task<SubscriptionDto> CreateSubscriptionAsync(
            string productHandle,
            string customerReference,
            string subscriptionReference,
            NoCardPaymentCollectionMethod paymentCollectionMethod,
            CancellationToken cancellationToken)
        {
            CreateSubscriptionCalls++;
            await Task.Delay(30, cancellationToken);
            if (CreateHasUnknownOutcome)
            {
                throw new MaxioUnknownOutcomeException("unknown");
            }

            _subscription = new SubscriptionDto(
                42,
                productHandle,
                "Pro",
                29900,
                "USD",
                1,
                IntervalUnit.Month.Value,
                "active",
                DateTimeOffset.UtcNow.AddMonths(1));
            return _subscription;
        }

        public Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(
            int customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionDto>>(
                _subscription is null ? Array.Empty<SubscriptionDto>() : new[] { _subscription });
    }
}
