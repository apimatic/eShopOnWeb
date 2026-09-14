#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string UserName = "demouser@microsoft.com";
    private const string ExpectedReference = "eshoponweb-demouser@microsoft.com";

    private readonly FakeMaxioApiClient _client = new();
    private readonly MaxioSubscriptionBillingService _service;

    public MaxioSubscriptionBillingServiceTests()
    {
        _client.AddProduct("eshop-pro", "Pro Plan", 29900);
        _client.AddProduct("basic-plan", "Basic Plan", 2900);

        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = "family",
            CustomerReferencePrefix = "eshoponweb",
            CatalogCacheSeconds = 0
        });

        _service = new MaxioSubscriptionBillingService(_client, options,
            new MemoryCache(new MemoryCacheOptions()), new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static SubscriberIdentity Subscriber => new(UserName);

    private Task<SubscribeResult> SubscribeAsync(string planHandle = "eshop-pro", string? idempotencyKey = null) =>
        _service.SubscribeAsync(new SubscribeRequest(Subscriber, planHandle, idempotencyKey));

    [Fact]
    public async Task ListsOnlyNonArchivedPlansCheapestFirstAndLabelsThemWithTheSiteCurrency()
    {
        _client.AddProduct("retired-plan", "Retired", 100, archivedAt: DateTimeOffset.UtcNow.AddDays(-1));

        var plans = await _service.GetPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle));
        Assert.All(plans, plan => Assert.Equal("USD", plan.Currency));
        Assert.Equal(299m, plans.Single(plan => plan.Handle == "eshop-pro").Price);
    }

    [Fact]
    public async Task SubscribingCreatesTheBillingCustomerOnceAndDerivesItsReferenceFromTheUser()
    {
        var result = await SubscribeAsync();

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(1, _client.CreateCustomerCalls);
        Assert.Equal(ExpectedReference, result.Subscription.CustomerReference);
        Assert.NotNull(await _client.FindCustomerByReferenceAsync(ExpectedReference));
    }

    [Fact]
    public async Task SubscribingReusesAnExistingBillingCustomer()
    {
        var seeded = _client.SeedCustomer(ExpectedReference);

        var result = await SubscribeAsync();

        Assert.Equal(0, _client.CreateCustomerCalls);
        Assert.Equal(seeded.Id, result.Subscription.CustomerId);
    }

    [Fact]
    public async Task InvoicesRatherThanAutoChargingSoSignupNeedsNoCardOnFile()
    {
        await SubscribeAsync();

        Assert.Equal("remittance", Assert.Single(_client.SubmittedPaymentCollectionMethods));
    }

    [Fact]
    public async Task UsesInvoiceCollectionOnLegacyStatementsSites()
    {
        _client.Site.RelationshipInvoicingEnabled = false;

        await SubscribeAsync();

        Assert.Equal("invoice", Assert.Single(_client.SubmittedPaymentCollectionMethods));
    }

    [Fact]
    public async Task RefusesPlansThatRequireAStoredPaymentMethodInsteadOfLettingTheChargeFail()
    {
        _client.AddProduct("card-required", "Card Required", 1000, requireCreditCard: true);

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => SubscribeAsync("card-required"));

        Assert.Contains("requires a payment method", exception.Message);
        Assert.Equal(0, _client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task RepeatingSubscribeReturnsTheExistingSubscriptionInsteadOfCreatingASecond()
    {
        var first = await SubscribeAsync();
        var second = await SubscribeAsync();

        Assert.False(first.AlreadySubscribed);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, _client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task ConcurrentSubscribesCollapseIntoASingleSubscription()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SubscribeAsync()));

        Assert.Equal(1, _client.CreateSubscriptionCalls);
        Assert.Equal(1, _client.CreateCustomerCalls);
        Assert.Single(attempts.Select(attempt => attempt.Subscription.Id).Distinct());
        Assert.Single(attempts.Where(attempt => !attempt.AlreadySubscribed));
    }

    [Fact]
    public async Task DeduplicationIsPerPlanSoASecondPlanStillEnrolls()
    {
        await SubscribeAsync("eshop-pro");
        var second = await SubscribeAsync("basic-plan");

        Assert.False(second.AlreadySubscribed);
        Assert.Equal(2, _client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task ACanceledSubscriptionDoesNotBlockSigningUpAgain()
    {
        var customer = _client.SeedCustomer(ExpectedReference);
        _client.SeedSubscription(customer.Id, "eshop-pro", "canceled");

        var result = await SubscribeAsync();

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(1, _client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SendsAUniquenessTokenSoARetriedWriteCannotDuplicate()
    {
        await SubscribeAsync();

        var token = Assert.Single(_client.SubmittedSubscriptionTokens);
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.StartsWith("subscribe-", token);
    }

    [Fact]
    public async Task ADuplicateSubmissionIsReconciledToTheSubscriptionItAlreadyCreated()
    {
        var customer = _client.SeedCustomer(ExpectedReference);

        // Maxio answers 409 because an equivalent submission already landed - and it did: the
        // subscription it produced shows up on the customer before we look again.
        var existing = 0L;
        _client.OnCreateSubscription = attempt =>
        {
            if (attempt != 1)
            {
                return null;
            }

            existing = _client.SeedSubscription(customer.Id, "eshop-pro", "active").Id;
            return new BillingConflictException("DuplicatePrevention::DuplicateSubmissionError");
        };

        var result = await SubscribeAsync();

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(existing, result.Subscription.Id);
    }

    [Fact]
    public async Task ADuplicateSubmissionThatLeftNoSubscriptionIsRetriedUnderAFreshToken()
    {
        _client.OnCreateSubscription = attempt =>
            attempt == 1 ? new BillingConflictException("DuplicatePrevention::DuplicateSubmissionError") : null;

        var result = await SubscribeAsync();

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(2, _client.CreateSubscriptionCalls);
        Assert.Equal(2, _client.SubmittedSubscriptionTokens.Distinct().Count());
    }

    [Fact]
    public async Task ACallerSuppliedIdempotencyKeyIsNeverQuietlyRetriedUnderADifferentToken()
    {
        _client.OnCreateSubscription = _ => new BillingConflictException("DuplicatePrevention::DuplicateSubmissionError");

        await Assert.ThrowsAsync<BillingConflictException>(() => SubscribeAsync(idempotencyKey: "abc-123"));

        Assert.Equal(1, _client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task LosingTheRaceToCreateTheCustomerResolvesToTheWinnersCustomer()
    {
        // Another caller claims the reference between our lookup and our write, so Maxio rejects ours.
        long winnerId = 0;
        _client.OnCreateCustomer = _ =>
        {
            winnerId = _client.SeedCustomer(ExpectedReference).Id;
            return new BillingValidationException("Reference: must be unique.");
        };

        var result = await SubscribeAsync();

        Assert.NotEqual(0, winnerId);
        Assert.Equal(winnerId, result.Subscription.CustomerId);
        Assert.Equal(1, _client.CreateCustomerCalls);
    }

    [Fact]
    public async Task UnknownPlanHandlesAreRejectedBeforeAnythingIsCreated()
    {
        var exception = await Assert.ThrowsAsync<PlanNotFoundException>(() => SubscribeAsync("no-such-plan"));

        Assert.Equal("no-such-plan", exception.PlanHandle);
        Assert.Equal(0, _client.CreateCustomerCalls);
        Assert.Equal(0, _client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task AShopperWhoNeverSubscribedGetsAnEmptyListAndNoBillingCustomer()
    {
        var subscriptions = await _service.GetSubscriptionsAsync(Subscriber);

        Assert.Empty(subscriptions);
        Assert.Equal(0, _client.CreateCustomerCalls);
    }

    [Fact]
    public async Task SubscriptionsComeBackNewestFirstWithTheBillingFactsAShopperNeeds()
    {
        await SubscribeAsync("eshop-pro");
        await SubscribeAsync("basic-plan");

        var subscriptions = await _service.GetSubscriptionsAsync(Subscriber);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal("basic-plan", subscriptions[0].PlanHandle);

        var pro = subscriptions.Single(subscription => subscription.PlanHandle == "eshop-pro");
        Assert.Equal("active", pro.State);
        Assert.True(pro.IsLive);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("USD", pro.Currency);
        Assert.NotNull(pro.NextBillingAt);
    }
}
