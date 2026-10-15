#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;
using static Microsoft.eShopWeb.IntegrationTests.Billing.MaxioTestHarness;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class SubscribeTests
{
    private const int CustomerId = 501;

    [Fact]
    public async Task CreatesCustomerAndSubscriptionOnce()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();
        string? host = null;
        harness.Stub.On(HttpMethod.Post, SubscriptionsPath, (request, body) =>
        {
            host = request.RequestUri!.Host;
            return Task.FromResult(MaxioStub.Json(HttpStatusCode.Created,
                MaxioStub.SubscriptionJson(9001, MaxioStub.SubscriptionReferenceIn(body), CustomerId, "eshop-pro")));
        });

        var result = await harness.NewService().SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(9001, result.Subscription!.SubscriptionId);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);
        Assert.Equal("maxio.test", host); // Maxio:BaseUrl used verbatim

        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, CustomersPath));
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, SubscriptionsPath));

        var customerBody = JsonDocument.Parse(harness.Stub.Requests.Single(r => r.Path == CustomersPath).Body!).RootElement.GetProperty("customer");
        Assert.Equal(UserName, customerBody.GetProperty("email").GetString());
        Assert.Equal(MaxioCustomerProvisioner.CustomerReferenceFor(UserName), customerBody.GetProperty("reference").GetString());

        var subscriptionBody = JsonDocument.Parse(harness.Stub.Requests.Single(r => r.Path == SubscriptionsPath).Body!).RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", subscriptionBody.GetProperty("product_handle").GetString());
        Assert.Equal(CustomerId, subscriptionBody.GetProperty("customer_id").GetInt32());
        Assert.Equal("remittance", subscriptionBody.GetProperty("payment_collection_method").GetString());
        Assert.StartsWith("eshop-sub-", subscriptionBody.GetProperty("reference").GetString());

        await using var db = harness.NewDbContext();
        var enrollment = await db.SubscriptionEnrollments.SingleAsync();
        Assert.Equal(BillingClaimStatus.Completed, enrollment.Status);
        Assert.Equal(9001, enrollment.ProviderSubscriptionId);
        var link = await db.BillingCustomerLinks.SingleAsync();
        Assert.Equal(CustomerId, link.ProviderCustomerId);
    }

    [Fact]
    public async Task ReplayReturnsExistingSubscriptionWithoutCreatingAnother()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();
        string? created = null;
        harness.Stub
            .On(HttpMethod.Post, SubscriptionsPath, (_, body) =>
            {
                created = MaxioStub.SubscriptionJson(9001, MaxioStub.SubscriptionReferenceIn(body), CustomerId, "eshop-pro");
                return Task.FromResult(MaxioStub.Json(HttpStatusCode.Created, created));
            })
            .On(HttpMethod.Get, CustomerSubscriptionsPath(CustomerId), (_, _) => Task.FromResult(MaxioStub.Json(HttpStatusCode.OK,
                MaxioStub.SubscriptionListJson(created is null ? Array.Empty<string>() : new[] { created }))));

        var first = await harness.NewService().SubscribeAsync(UserName, "eshop-pro");
        var second = await harness.NewService().SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, first.Outcome);
        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(9001, second.Subscription!.SubscriptionId);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, CustomersPath));
    }

    [Fact]
    public async Task ConcurrentDoubleClickReachesMaxioOnce()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();
        string? created = null;
        harness.Stub
            .On(HttpMethod.Post, SubscriptionsPath, async (_, body) =>
            {
                await Task.Delay(500);
                created = MaxioStub.SubscriptionJson(9001, MaxioStub.SubscriptionReferenceIn(body), CustomerId, "eshop-pro");
                return MaxioStub.Json(HttpStatusCode.Created, created);
            })
            .On(HttpMethod.Get, CustomerSubscriptionsPath(CustomerId), (_, _) => Task.FromResult(MaxioStub.Json(HttpStatusCode.OK,
                MaxioStub.SubscriptionListJson(created is null ? Array.Empty<string>() : new[] { created }))));

        var attempts = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => harness.NewService().SubscribeAsync(UserName, "eshop-pro")))
            .ToArray();
        var outcomes = await Task.WhenAll(attempts.Select(async t =>
        {
            try { return (await t).Outcome.ToString(); }
            catch (SubscriptionInProgressException) { return "InProgress"; }
        }));

        Assert.Contains(nameof(SubscribeOutcome.Created), outcomes);
        Assert.Single(outcomes, o => o == nameof(SubscribeOutcome.Created));
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.True(harness.Stub.Count(HttpMethod.Post, CustomersPath) <= 1);
    }

    [Fact]
    public async Task UnknownPlanIsRejectedBeforeAnyWrite()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => harness.NewService().SubscribeAsync(UserName, "not-offered"));

        Assert.Equal(0, harness.Stub.Count(HttpMethod.Post, CustomersPath));
        Assert.Equal(0, harness.Stub.Count(HttpMethod.Post, SubscriptionsPath));
        await using var db = harness.NewDbContext();
        Assert.Empty(await db.SubscriptionEnrollments.ToListAsync());
    }

    [Fact]
    public async Task ProviderRejectionReleasesClaimSoTheUserCanRetry()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();
        harness.Stub.On(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.NewService().SubscribeAsync(UserName, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Contains("No payment method was on file for the $299.00 balance", ex.Reasons);
        await using (var db = harness.NewDbContext())
        {
            Assert.Empty(await db.SubscriptionEnrollments.ToListAsync());
        }

        harness.Stub.On(HttpMethod.Post, SubscriptionsPath, (_, body) => Task.FromResult(MaxioStub.Json(HttpStatusCode.Created,
            MaxioStub.SubscriptionJson(9002, MaxioStub.SubscriptionReferenceIn(body), CustomerId, "eshop-pro"))));
        var retry = await harness.NewService().SubscribeAsync(UserName, "eshop-pro");
        Assert.Equal(SubscribeOutcome.Created, retry.Outcome);
    }

    [Fact]
    public async Task ConnectionFailureOnCreateIsSettledByReference()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();
        string? sentReference = null;
        harness.Stub
            .On(HttpMethod.Post, SubscriptionsPath, (_, body) =>
            {
                sentReference = MaxioStub.SubscriptionReferenceIn(body);
                throw new HttpRequestException("connection reset after the request was sent");
            })
            .On(HttpMethod.Get, SubscriptionLookupPath, (request, _) =>
            {
                Assert.Contains("reference=" + sentReference, request.RequestUri!.Query);
                return Task.FromResult(MaxioStub.Json(HttpStatusCode.OK,
                    MaxioStub.SubscriptionJson(9003, sentReference, CustomerId, "eshop-pro")));
            });

        var result = await harness.NewService().SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(9003, result.Subscription!.SubscriptionId);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, SubscriptionsPath));
        await using var db = harness.NewDbContext();
        Assert.Equal(BillingClaimStatus.Completed, (await db.SubscriptionEnrollments.SingleAsync()).Status);
    }

    [Fact]
    public async Task UnsettledOutcomeIsPendingAndTheNextRequestSettlesItWithoutCreatingAgain()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();
        string? sentReference = null;
        harness.Stub
            .On(HttpMethod.Post, SubscriptionsPath, (_, body) =>
            {
                sentReference = MaxioStub.SubscriptionReferenceIn(body);
                throw new HttpRequestException("connection reset");
            })
            .On(HttpMethod.Get, SubscriptionLookupPath, (_, _) => throw new HttpRequestException("still unreachable"));

        var first = await harness.NewService().SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(SubscribeOutcome.PendingConfirmation, first.Outcome);
        Assert.Null(first.Subscription);
        await using (var db = harness.NewDbContext())
        {
            Assert.Equal(BillingClaimStatus.Unknown, (await db.SubscriptionEnrollments.SingleAsync()).Status);
        }

        harness.Stub.On(HttpMethod.Get, SubscriptionLookupPath, (_, _) => Task.FromResult(MaxioStub.Json(HttpStatusCode.OK,
            MaxioStub.SubscriptionJson(9004, sentReference, CustomerId, "eshop-pro"))));
        var second = await harness.NewService().SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(9004, second.Subscription!.SubscriptionId);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task ConnectionFailureWhenNothingWasCreatedReleasesTheClaim()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();
        harness.Stub
            .On(HttpMethod.Post, SubscriptionsPath, (_, _) => throw new HttpRequestException("connection refused"))
            .On(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.NotFound, "");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.NewService().SubscribeAsync(UserName, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.IsNotType<BillingOutcomeUnknownException>(ex);
        await using var db = harness.NewDbContext();
        Assert.Empty(await db.SubscriptionEnrollments.ToListAsync());
    }

    [Fact]
    public async Task CustomerCreateWithLostResponseIsSettledByCustomerReference()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();
        var reference = MaxioCustomerProvisioner.CustomerReferenceFor(UserName);
        var customerLanded = false;
        harness.Stub
            .On(HttpMethod.Get, CustomerLookupPath, (_, _) => Task.FromResult(customerLanded
                ? MaxioStub.Json(HttpStatusCode.OK, MaxioStub.CustomerJson(CustomerId, reference))
                : MaxioStub.Json(HttpStatusCode.NotFound, "{}")))
            .On(HttpMethod.Post, CustomersPath, (_, _) =>
            {
                customerLanded = true;
                throw new HttpRequestException("connection reset after the customer was created");
            })
            .On(HttpMethod.Post, SubscriptionsPath, (_, body) => Task.FromResult(MaxioStub.Json(HttpStatusCode.Created,
                MaxioStub.SubscriptionJson(9005, MaxioStub.SubscriptionReferenceIn(body), CustomerId, "eshop-pro"))));

        var result = await harness.NewService().SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, CustomersPath));
        await using var db = harness.NewDbContext();
        var link = await db.BillingCustomerLinks.SingleAsync();
        Assert.Equal(BillingClaimStatus.Completed, link.Status);
        Assert.Equal(CustomerId, link.ProviderCustomerId);
    }

    [Fact]
    public async Task CustomerDuplicateReferenceRejectionReusesTheExistingCustomer()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();
        var reference = MaxioCustomerProvisioner.CustomerReferenceFor(UserName);
        var lookups = 0;
        harness.Stub
            .On(HttpMethod.Get, CustomerLookupPath, (_, _) => Task.FromResult(Interlocked.Increment(ref lookups) == 1
                ? MaxioStub.Json(HttpStatusCode.NotFound, "{}")
                : MaxioStub.Json(HttpStatusCode.OK, MaxioStub.CustomerJson(CustomerId, reference))))
            .On(HttpMethod.Post, CustomersPath, HttpStatusCode.UnprocessableEntity, """{"errors":["Reference must be unique"]}""")
            .On(HttpMethod.Post, SubscriptionsPath, (_, body) => Task.FromResult(MaxioStub.Json(HttpStatusCode.Created,
                MaxioStub.SubscriptionJson(9006, MaxioStub.SubscriptionReferenceIn(body), CustomerId, "eshop-pro"))));

        var result = await harness.NewService().SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        var subscriptionBody = JsonDocument.Parse(harness.Stub.Requests.Single(r => r.Path == SubscriptionsPath).Body!).RootElement;
        Assert.Equal(CustomerId, subscriptionBody.GetProperty("subscription").GetProperty("customer_id").GetInt32());
    }

    [Fact]
    public async Task LiveSubscriptionAlreadyAtMaxioIsAdoptedAfterTheLocalStoreWasLost()
    {
        var harness = new MaxioTestHarness().WithNewCustomerHappyPath();
        var reference = MaxioCustomerProvisioner.CustomerReferenceFor(UserName);
        harness.Stub
            .On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, MaxioStub.CustomerJson(CustomerId, reference))
            .On(HttpMethod.Get, CustomerSubscriptionsPath(CustomerId), HttpStatusCode.OK,
                MaxioStub.SubscriptionListJson(new[] { MaxioStub.SubscriptionJson(8001, "eshop-sub-from-an-earlier-run", CustomerId, "eshop-pro") }));

        var result = await harness.NewService().SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(8001, result.Subscription!.SubscriptionId);
        Assert.Equal(0, harness.Stub.Count(HttpMethod.Post, CustomersPath));
        Assert.Equal(0, harness.Stub.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task CreateIsSentOnceEvenWithProductionRetrySettings()
    {
        var harness = new MaxioTestHarness(productionRetries: true).WithNewCustomerHappyPath();
        harness.Stub
            .On(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.ServiceUnavailable, "")
            .On(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.NotFound, "");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.NewService().SubscribeAsync(UserName, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Get, SubscriptionLookupPath));
    }

    [Fact]
    public async Task DuplicateClaimInsertIsRefusedByTheStore()
    {
        var harness = new MaxioTestHarness();
        var now = DateTimeOffset.UtcNow;
        await using var first = harness.NewDbContext();
        await using var second = harness.NewDbContext();
        first.SubscriptionEnrollments.Add(new SubscriptionEnrollment("USER", "eshop-pro", "ref-1", now));
        await first.SaveChangesAsync();

        second.SubscriptionEnrollments.Add(new SubscriptionEnrollment("USER", "eshop-pro", "ref-2", now));
        var refusal = await Record.ExceptionAsync(() => second.SaveChangesAsync());

        // SQL Server raises DbUpdateException; the EF in-memory provider raises ArgumentException. Both are caught as a claim conflict.
        Assert.True(refusal is DbUpdateException or ArgumentException, refusal?.GetType().FullName);
    }
}
