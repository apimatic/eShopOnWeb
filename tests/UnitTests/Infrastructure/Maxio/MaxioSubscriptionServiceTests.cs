using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Tests for the idempotent Maxio subscription orchestration:
/// customer enrollment, plan validation, cardless signup fallback and replay behavior.
/// </summary>
public class MaxioSubscriptionServiceTests
{
    private static UserManager<ApplicationUser> CreateUserManagerThatReturns(ApplicationUser user)
    {
        var userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null, null, null, null, null, null, null, null);
        userManager.FindByNameAsync(Arg.Any<string>()).Returns(user);
        return userManager;
    }

    private static AppIdentityDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"subscriptions-{Guid.NewGuid():N}")
            .Options);

    private static ApplicationUser CreateUser() => new()
    {
        Id = MaxioTestData.UserId,
        UserName = MaxioTestData.UserEmail,
        Email = MaxioTestData.UserEmail
    };

    private static MaxioSubscriptionService CreateService(
        FakeMaxioClient client,
        AppIdentityDbContext db,
        UserManager<ApplicationUser> userManager) =>
        new(client, db, userManager, Options.Create(MaxioTestData.Options), NullLogger<MaxioSubscriptionService>.Instance);

    [Fact]
    public async Task ListPlansAsync_FiltersProductsToConfiguredFamilyAndExcludesArchived()
    {
        var client = new FakeMaxioClient();
        client.Products.Add(MaxioTestData.Product("eshop-pro", "Pro Plan", 29900));
        client.Products.Add(MaxioTestData.Product("other", "Other Plan", 100, familyHandle: "some-other-family"));
        var archived = MaxioTestData.Product("archived-plan", "Archived Plan", 100);
        archived.ArchivedAt = DateTimeOffset.UtcNow;
        client.Products.Add(archived);
        var db = CreateDbContext();
        var service = CreateService(client, db, CreateUserManagerThatReturns(CreateUser()));

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("299.00", plan.Price);
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_ThrowsPlanNotFound()
    {
        var client = new FakeMaxioClient();
        client.Products.Add(MaxioTestData.Product("eshop-pro", "Pro Plan", 29900));
        var db = CreateDbContext();
        var service = CreateService(client, db, CreateUserManagerThatReturns(CreateUser()));

        await Assert.ThrowsAsync<MaxioPlanNotFoundException>(() => service.SubscribeAsync(MaxioTestData.UserEmail, "no-such-plan"));
        Assert.Empty(client.SubscriptionCreateCalls);
        Assert.Empty(db.UserSubscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_FirstSubscribe_CreatesCustomerThenSubscriptionAndPersistsRecord()
    {
        var client = new FakeMaxioClient
        {
            CustomerFoundByReference = null
        };
        client.Products.Add(MaxioTestData.Product("eshop-pro", "Pro Plan", 29900));
        client.CreateCustomerResponses.Enqueue(new MaxioCustomer { Id = 42, Reference = MaxioTestData.UserId, Email = MaxioTestData.UserEmail });
        client.CreateSubscriptionResponses.Enqueue(MaxioTestData.Subscription(1001, 42, reference: $"{MaxioTestData.UserId}:eshop-pro"));
        var db = CreateDbContext();
        var service = CreateService(client, db, CreateUserManagerThatReturns(CreateUser()));

        var result = await service.SubscribeAsync(MaxioTestData.UserEmail, "eshop-pro");

        Assert.False(result.WasExisting);
        Assert.Equal(1001, result.MaxioSubscriptionId);
        Assert.Equal(42, result.MaxioCustomerId);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal("active", result.State);
        Assert.NotNull(result.NextBillingAt);
        var call = Assert.Single(client.SubscriptionCreateCalls);
        Assert.Equal("eshop-pro", call.ProductHandle);
        Assert.Equal(42, call.CustomerId);
        Assert.Equal($"{MaxioTestData.UserId}:eshop-pro", call.Reference);
        Assert.Null(call.PaymentCollectionMethod);
        var record = Assert.Single(db.UserSubscriptions);
        Assert.Equal(MaxioTestData.UserId, record.UserId);
        Assert.Equal(1001, record.MaxioSubscriptionId);
        Assert.Equal(42, record.MaxioCustomerId);
        Assert.Equal("active", record.State);
    }

    [Fact]
    public async Task SubscribeAsync_RepeatRequest_ReplaysExistingSubscriptionWithoutCallingMaxioAgain()
    {
        var client = new FakeMaxioClient();
        client.Products.Add(MaxioTestData.Product("eshop-pro", "Pro Plan", 29900));
        client.CustomerFoundByReference = new MaxioCustomer { Id = 42, Reference = MaxioTestData.UserId };
        client.CreateSubscriptionResponses.Enqueue(MaxioTestData.Subscription(1001, 42));
        var db = CreateDbContext();
        db.UserSubscriptions.Add(new UserSubscription
        {
            UserId = MaxioTestData.UserId,
            MaxioCustomerId = 42,
            MaxioSubscriptionId = 1001,
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            PriceInCents = 29900,
            State = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(client, db, CreateUserManagerThatReturns(CreateUser()));

        var result = await service.SubscribeAsync(MaxioTestData.UserEmail, "eshop-pro");

        Assert.True(result.WasExisting);
        Assert.Equal(1001, result.MaxioSubscriptionId);
        Assert.Empty(client.SubscriptionCreateCalls);
    }

    [Fact]
    public async Task SubscribeAsync_PaymentRequiredBySite_RetriesWithRemittance()
    {
        var client = new FakeMaxioClient();
        client.Products.Add(MaxioTestData.Product("eshop-pro", "Pro Plan", 29900));
        client.CustomerFoundByReference = new MaxioCustomer { Id = 42, Reference = MaxioTestData.UserId };
        client.CreateSubscriptionResponses.Enqueue(MaxioTestData.PaymentRequiredError);
        client.CreateSubscriptionResponses.Enqueue(MaxioTestData.Subscription(1001, 42));
        var db = CreateDbContext();
        var service = CreateService(client, db, CreateUserManagerThatReturns(CreateUser()));

        var result = await service.SubscribeAsync(MaxioTestData.UserEmail, "eshop-pro");

        Assert.False(result.WasExisting);
        Assert.Equal(2, client.CreateSubscriptionCallCount);
        Assert.Null(client.SubscriptionCreateCalls[0].PaymentCollectionMethod);
        Assert.Equal("remittance", client.SubscriptionCreateCalls[1].PaymentCollectionMethod);
        Assert.Equal(1001, result.MaxioSubscriptionId);
    }

    [Fact]
    public async Task SubscribeAsync_PaymentErrorOfDifferentKind_DoesNotRetryWithRemittance()
    {
        var client = new FakeMaxioClient();
        client.Products.Add(MaxioTestData.Product("eshop-pro", "Pro Plan", 29900));
        client.CustomerFoundByReference = new MaxioCustomer { Id = 42, Reference = MaxioTestData.UserId };
        client.CreateSubscriptionResponses.Enqueue(new MaxioApiException(422, new List<string> { "Product: cannot be blank." }, "422"));
        var db = CreateDbContext();
        var service = CreateService(client, db, CreateUserManagerThatReturns(CreateUser()));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => service.SubscribeAsync(MaxioTestData.UserEmail, "eshop-pro"));
        Assert.Equal(1, client.CreateSubscriptionCallCount);
        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_MaxioReferenceRace_RecoversAndReplaysSubscription()
    {
        var client = new FakeMaxioClient();
        client.Products.Add(MaxioTestData.Product("eshop-pro", "Pro Plan", 29900));
        client.CustomerFoundByReference = new MaxioCustomer { Id = 42, Reference = MaxioTestData.UserId };
        client.CreateSubscriptionResponses.Enqueue(MaxioTestData.ReferenceTakenError);
        var db = CreateDbContext();
        var service = CreateService(client, db, CreateUserManagerThatReturns(CreateUser()));
        // Simulate the concurrent "winner": it persisted its subscription row while our
        // request was in flight, so Maxio rejects our duplicate reference.
        client.OnSubscriptionCreate = () =>
        {
            db.UserSubscriptions.Add(new UserSubscription
            {
                UserId = MaxioTestData.UserId,
                MaxioCustomerId = 42,
                MaxioSubscriptionId = 1001,
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                PriceInCents = 29900,
                State = "active",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.SaveChanges();
        };

        var result = await service.SubscribeAsync(MaxioTestData.UserEmail, "eshop-pro");

        Assert.True(result.WasExisting);
        Assert.Equal(1001, result.MaxioSubscriptionId);
        Assert.Equal(1, client.CreateSubscriptionCallCount);
    }

    [Fact]
    public async Task SubscribeAsync_CustomerCreatedConcurrently_ReusesExistingMaxioCustomer()
    {
        var client = new FakeMaxioClient();
        client.Products.Add(MaxioTestData.Product("eshop-pro", "Pro Plan", 29900));
        // Lookup #1: none. Create: reference-taken (created concurrently). Lookup #2: the customer now exists.
        client.CreateCustomerResponses.Enqueue(MaxioTestData.CustomerReferenceTakenError);
        client.LookupResults = new Queue<MaxioCustomer?>(new MaxioCustomer?[] { null, new MaxioCustomer { Id = 42, Reference = MaxioTestData.UserId } });
        client.CreateSubscriptionResponses.Enqueue(MaxioTestData.Subscription(1001, 42));
        var db = CreateDbContext();
        var service = CreateService(client, db, CreateUserManagerThatReturns(CreateUser()));

        var result = await service.SubscribeAsync(MaxioTestData.UserEmail, "eshop-pro");

        Assert.False(result.WasExisting);
        Assert.Equal(42, result.MaxioCustomerId);
        Assert.Equal(1001, result.MaxioSubscriptionId);
    }

    [Fact]
    public async Task ListForUserAsync_NoSubscriptions_ReturnsEmptyList()
    {
        var client = new FakeMaxioClient();
        var db = CreateDbContext();
        var service = CreateService(client, db, CreateUserManagerThatReturns(CreateUser()));

        var result = await service.ListForUserAsync(MaxioTestData.UserEmail);

        Assert.Empty(result);
        Assert.Empty(client.CustomerLookups);
    }

    [Fact]
    public async Task ListForUserAsync_WithSubscription_MapsRecordToResult()
    {
        var client = new FakeMaxioClient();
        client.CustomerSubscriptions.Add(MaxioTestData.Subscription(1001, 42));
        var db = CreateDbContext();
        db.UserSubscriptions.Add(new UserSubscription
        {
            UserId = MaxioTestData.UserId,
            MaxioCustomerId = 42,
            MaxioSubscriptionId = 1001,
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            PriceInCents = 29900,
            State = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(client, db, CreateUserManagerThatReturns(CreateUser()));

        var result = await service.ListForUserAsync(MaxioTestData.UserEmail);

        var subscription = Assert.Single(result);
        Assert.Equal(1001, subscription.MaxioSubscriptionId);
        Assert.Equal(42, subscription.MaxioCustomerId);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal("active", subscription.State);
        Assert.NotNull(subscription.NextBillingAt);
    }
}