using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string UserId = "user-1";
    private const string PlanHandle = "eshop-pro";

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscriptionOnce()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(PlanHandle, result.Subscription.PlanHandle);
        Assert.Equal("Pro", result.Subscription.PlanName);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);
        Assert.Equal(result.Subscription.NextBillingAt, result.Subscription.NextAssessmentAt);
        Assert.Equal(SubscriptionReference.For(UserId, PlanHandle), result.Subscription.Reference);
        Assert.Equal(1, fixture.Handler.CustomerPosts);
        Assert.Equal(1, fixture.Handler.SubscriptionPosts);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", fixture.Handler.LastSubscriptionBody);
        Assert.Contains("\"customer_reference\":\"user-1\"", fixture.Handler.LastSubscriptionBody);
        Assert.Contains("\"reference\":\"user-1:eshop-pro\"", fixture.Handler.LastSubscriptionBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", fixture.Handler.LastSubscriptionBody);
        Assert.DoesNotContain("payment_profile", fixture.Handler.LastSubscriptionBody);
        Assert.DoesNotContain("credit_card", fixture.Handler.LastSubscriptionBody);
        Assert.Equal(BillingClaimStatus.Completed, await ClaimStatus(fixture, UserId));
        Assert.Equal(BillingClaimStatus.Completed, await ClaimStatus(fixture, SubscriptionReference.For(UserId, PlanHandle)));
    }

    [Fact]
    public async Task Subscribe_SecondCallDoesNotCreateAgain()
    {
        var fixture = new Fixture();
        await fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None);
        var again = await fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None);

        Assert.False(again.Created);
        Assert.Equal("active", again.Subscription.State);
        Assert.Equal(29900, again.Subscription.PriceInCents);
        Assert.Equal(1, fixture.Handler.CustomerPosts);
        Assert.Equal(1, fixture.Handler.SubscriptionPosts);
    }

    [Fact]
    public async Task Subscribe_InProgressCustomerClaimIsRejectedBeforeCreate()
    {
        var fixture = new Fixture();
        fixture.Db.BillingClaims.Add(new BillingClaim(UserId));
        await fixture.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DuplicateException>(() =>
            fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("already in progress", ex.Message);
        Assert.Equal(0, fixture.Handler.CustomerPosts);
        Assert.Equal(0, fixture.Handler.SubscriptionPosts);
    }

    [Fact]
    public async Task Subscribe_UnknownCustomerIsNotCreatedAgain()
    {
        var fixture = new Fixture();
        fixture.Handler.FailCustomerPost = true;

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(() =>
            fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(BillingClaimStatus.Unknown, await ClaimStatus(fixture, UserId));

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(() =>
            fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(1, fixture.Handler.CustomerPosts);
        Assert.Equal(0, fixture.Handler.SubscriptionPosts);
        Assert.Equal(BillingClaimStatus.Unknown, await ClaimStatus(fixture, UserId));
    }

    [Fact]
    public async Task Subscribe_CustomerConnectionFailureSettlesWhenTheCustomerLanded()
    {
        var fixture = new Fixture();
        fixture.Handler.FailCustomerPost = true;
        fixture.Handler.CustomerLandsOnFailedPost = true;

        var result = await fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(1, fixture.Handler.CustomerPosts);
        Assert.Equal(1, fixture.Handler.SubscriptionPosts);
        Assert.Equal(BillingClaimStatus.Completed, await ClaimStatus(fixture, UserId));
    }

    [Fact]
    public async Task Subscribe_UnknownSubscriptionIsNotCreatedAgain()
    {
        var fixture = new Fixture();
        fixture.Handler.CustomerExists = true;
        fixture.Handler.FailSubscriptionPost = true;
        var reference = SubscriptionReference.For(UserId, PlanHandle);

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(() =>
            fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(BillingClaimStatus.Unknown, await ClaimStatus(fixture, reference));

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(() =>
            fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(0, fixture.Handler.CustomerPosts);
        Assert.Equal(1, fixture.Handler.SubscriptionPosts);
    }

    [Fact]
    public async Task Subscribe_SubscriptionConnectionFailureSettlesWhenItLanded()
    {
        var fixture = new Fixture();
        fixture.Handler.CustomerExists = true;
        fixture.Handler.FailSubscriptionPost = true;
        fixture.Handler.SubscriptionLandsOnFailedPost = true;

        var result = await fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(PlanHandle, result.Subscription.PlanHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Equal(1, fixture.Handler.SubscriptionPosts);
        Assert.Equal(BillingClaimStatus.Completed, await ClaimStatus(fixture, SubscriptionReference.For(UserId, PlanHandle)));
    }

    [Fact]
    public async Task Subscribe_RejectsAHandleTheCatalogDidNotReturn()
    {
        var fixture = new Fixture();

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() =>
            fixture.Service.SubscribeAsync(Shopper(), "not-a-plan", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, fixture.Handler.CustomerPosts);
        Assert.Empty(await fixture.Db.BillingClaims.ToListAsync());
    }

    [Fact]
    public async Task Subscribe_DoesNotEnrollWhenThePlanRequiresACard()
    {
        var fixture = new Fixture();
        fixture.Handler.RequireCreditCard = true;

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() =>
            fixture.Service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, fixture.Handler.SubscriptionPosts);
        Assert.Empty(await fixture.Db.BillingClaims.ToListAsync());
    }

    [Fact]
    public async Task ListPlans_ReportsTruncationOnlyWhenThePageCapIsFull()
    {
        var full = new Fixture();
        full.Handler.ProductsPerPage = MaxioSubscriptionBillingService.PageSize;
        var truncated = await full.Service.ListPlansAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.True(truncated.Truncated);
        Assert.Equal(MaxioSubscriptionBillingService.PageSize * MaxioSubscriptionBillingService.PageCap, truncated.Plans.Count);

        var shortPage = new Fixture();
        var catalog = await shortPage.Service.ListPlansAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.False(catalog.Truncated);
        Assert.Equal(PlanHandle, catalog.Plans[0].Handle);
        Assert.Equal("month", catalog.Plans[0].IntervalUnit);
        Assert.Equal(29900, catalog.Plans[0].PriceInCents);
    }

    [Fact]
    public async Task ListMine_WithoutACustomerReturnsEmpty()
    {
        var fixture = new Fixture();
        var mine = await fixture.Service.ListMineAsync(UserId, Guid.NewGuid(), CancellationToken.None);
        Assert.Empty(mine);
        Assert.Equal(0, fixture.Handler.CustomerPosts);
    }

    [Fact]
    public async Task ParallelSubscribe_SendsEachWriteOnce()
    {
        var database = Guid.NewGuid().ToString();
        var handler = new BillingStub();
        var first = CreateService(handler, database);
        var second = CreateService(handler, database);

        var outcomes = await Task.WhenAll(
            Invoke(first.Service),
            Invoke(second.Service));

        Assert.Equal(1, handler.CustomerPosts);
        Assert.Equal(1, handler.SubscriptionPosts);
        Assert.Contains(outcomes, outcome => outcome is ShopperSubscription);
    }

    [Fact]
    public async Task ClaimPrimaryKey_RejectsTheSecondInsert()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new CatalogContext(options);
        db.BillingClaims.Add(new BillingClaim(UserId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.BillingClaims.Add(new BillingClaim(UserId));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        Assert.Contains("same key", ex.Message);
    }

    private static async Task<object> Invoke(MaxioSubscriptionBillingService service)
    {
        try
        {
            var result = await service.SubscribeAsync(Shopper(), PlanHandle, Guid.NewGuid(), CancellationToken.None);
            return result.Subscription;
        }
        catch (Exception ex) when (ex is DuplicateException or BillingOutcomeUnknownException)
        {
            return ex;
        }
    }

    private static async Task<BillingClaimStatus> ClaimStatus(Fixture fixture, string reference)
    {
        fixture.Db.ChangeTracker.Clear();
        var claim = await fixture.Db.BillingClaims.SingleAsync(row => row.Reference == reference);
        return claim.Status;
    }

    private static ShopperProfile Shopper() => new()
    {
        UserId = UserId,
        Email = "demouser@microsoft.com",
        FirstName = "Demouser",
        LastName = "Subscriber"
    };

    private static Fixture CreateService(BillingStub handler, string database)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(database).Options;
        var db = new CatalogContext(options);
        var http = new HttpClient(handler) { Timeout = MaxioClientFactory.AttemptTimeout };
        var client = new MaxioAdvancedBillingClient(
            http,
            MaxioClientFactory.CreateOptions(new MaxioSettings
            {
                ApiKey = "test-api-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "eshop-subscribe"
            }, NullLoggerFactory.Instance));
        var service = new MaxioSubscriptionBillingService(
            client,
            db,
            Options.Create(new MaxioSettings
            {
                ApiKey = "test-api-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "eshop-subscribe"
            }),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
        return new Fixture(service, db, handler);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
            : this(null, null, new BillingStub())
        {
            var created = CreateService(Handler, Guid.NewGuid().ToString());
            Service = created.Service;
            Db = created.Db;
        }

        public Fixture(MaxioSubscriptionBillingService service, CatalogContext db, BillingStub handler)
        {
            Service = service!;
            Db = db!;
            Handler = handler;
        }

        public MaxioSubscriptionBillingService Service { get; private set; }
        public CatalogContext Db { get; private set; }
        public BillingStub Handler { get; }

        public void Dispose() => Db.Dispose();
    }

    private sealed class BillingStub : HttpMessageHandler
    {
        private readonly object _gate = new();
        public int CustomerPosts { get; private set; }
        public int SubscriptionPosts { get; private set; }
        public bool CustomerExists { get; set; }
        public bool SubscriptionExists { get; set; }
        public bool FailCustomerPost { get; set; }
        public bool FailSubscriptionPost { get; set; }
        public bool CustomerLandsOnFailedPost { get; set; }
        public bool SubscriptionLandsOnFailedPost { get; set; }
        public bool RequireCreditCard { get; set; }
        public int ProductsPerPage { get; set; } = 1;
        public string? LastSubscriptionBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var path = request.RequestUri!.AbsolutePath;
                var body = request.Content == null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (path.Contains("/products.json", StringComparison.Ordinal))
                    return Task.FromResult(Json(HttpStatusCode.OK, Products(QueryPage(request.RequestUri))));

                if (path.Contains("/customers/lookup.json", StringComparison.Ordinal))
                    return Task.FromResult(CustomerExists
                        ? Json(HttpStatusCode.OK, CustomerJson())
                        : Json(HttpStatusCode.NotFound, "{}"));

                if (path.EndsWith("/customers.json", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
                {
                    CustomerPosts++;
                    if (FailCustomerPost)
                    {
                        if (CustomerLandsOnFailedPost)
                            CustomerExists = true;
                        throw new HttpRequestException("connection reset");
                    }

                    CustomerExists = true;
                    return Task.FromResult(Json(HttpStatusCode.Created, CustomerJson()));
                }

                if (path.Contains("/subscriptions/lookup.json", StringComparison.Ordinal))
                    return Task.FromResult(SubscriptionExists
                        ? Json(HttpStatusCode.OK, SubscriptionJson())
                        : Json(HttpStatusCode.NotFound, "{}"));

                if (path.EndsWith("/subscriptions.json", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
                {
                    SubscriptionPosts++;
                    LastSubscriptionBody = body;
                    if (FailSubscriptionPost)
                    {
                        if (SubscriptionLandsOnFailedPost)
                            SubscriptionExists = true;
                        throw new HttpRequestException("connection reset");
                    }

                    SubscriptionExists = true;
                    return Task.FromResult(Json(HttpStatusCode.Created, SubscriptionJson()));
                }

                if (path.Contains("/subscriptions.json", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                    return Task.FromResult(Json(HttpStatusCode.OK, SubscriptionExists ? "[" + SubscriptionJson() + "]" : "[]"));

                return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
            }
        }

        private string Products(int page)
        {
            if (page > 1 && ProductsPerPage < MaxioSubscriptionBillingService.PageSize)
                return "[]";

            var count = ProductsPerPage;
            var builder = new StringBuilder("[");
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                    builder.Append(',');
                var handle = count == 1 ? PlanHandle : "plan-" + page + "-" + i;
                var card = RequireCreditCard ? "true" : "false";
                builder.Append("{\"product\":{\"handle\":\"")
                    .Append(handle)
                    .Append("\",\"name\":\"Pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":")
                    .Append(card)
                    .Append("}}");
            }

            builder.Append(']');
            return builder.ToString();
        }

        private static string CustomerJson() =>
            "{\"customer\":{\"id\":42,\"reference\":\"" + UserId + "\",\"email\":\"demouser@microsoft.com\",\"first_name\":\"Demouser\",\"last_name\":\"Subscriber\"}}";

        private static string SubscriptionJson() =>
            "{\"subscription\":{\"reference\":\"" + SubscriptionReference.For(UserId, PlanHandle) + "\",\"state\":\"active\",\"product_price_in_cents\":29900,\"current_period_ends_at\":\"2026-11-19T00:00:00+00:00\",\"next_assessment_at\":\"2026-11-19T00:00:00+00:00\",\"product\":{\"handle\":\"" + PlanHandle + "\",\"name\":\"Pro\"}}}";

        private static int QueryPage(Uri uri)
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var bits = part.Split('=', 2);
                if (bits.Length == 2 && bits[0] == "page" && int.TryParse(bits[1], out var page))
                    return page;
            }

            return 1;
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
