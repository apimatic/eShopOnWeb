using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Billing;

public class MaxioSubscriptionServiceTests
{
    private const string User = "Demo.User@Example.com";            // normalized to lower case
    private const string UserKey = "demo.user@example.com";

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Harness
    {
        public FakeMaxio Maxio { get; } = new();
        public string DbName { get; } = Guid.NewGuid().ToString();
        public MutableClock Clock { get; } = new();

        public CatalogContext NewContext() =>
            new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(DbName).Options);

        public MaxioSubscriptionService NewService(CatalogContext? db = null)
        {
            var client = new MaxioAdvancedBillingClient(new HttpClient(Maxio), new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
                Retry = RetryOptions.Disabled(),
                Server = new ServerOptions { Production = { Us = { BaseUrl = "https://maxio.test" } } }
            });
            return new MaxioSubscriptionService(client, db ?? NewContext(),
                Options.Create(new MaxioOptions { ApiKey = "test-key", Subdomain = "x", ProductFamilyHandle = "test-family" }),
                NullLogger<MaxioSubscriptionService>.Instance, Clock);
        }
    }

    // ---- plans

    [Fact]
    public async Task ListPlans_ReturnsOnlyNonArchivedPlansOfTheConfiguredFamily()
    {
        var h = new Harness();
        h.Maxio.Plans.Add(new("old-plan", "Old", 100, Archived: true));

        var result = await h.NewService().ListPlansAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsPartial);
        Assert.Equal(new[] { "fake-basic", "fake-pro" }, result.Plans.Select(p => p.Handle));
        Assert.Equal(9900, result.Plans.Single(p => p.Handle == "fake-pro").PriceInCents);
        Assert.Equal("month", result.Plans[0].IntervalUnit);
        var request = h.Maxio.Requests.Single();
        Assert.Equal("test-family", System.Web.HttpUtility.UrlDecode(request.PathAndQuery).Split('/')[2].Replace("handle:", ""));
        Assert.Contains("per_page=200", request.PathAndQuery);
    }

    [Fact]
    public async Task ListPlans_FlagsPartialWhenThePageCapIsHit()
    {
        var h = new Harness();
        h.Maxio.Plans.Clear();
        for (var i = 0; i < 1000; i++) h.Maxio.Plans.Add(new($"p{i}", $"P{i}", 1));

        var result = await h.NewService().ListPlansAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Equal(5, h.Maxio.Requests.Count);
        Assert.Equal(1000, result.Plans.Count);
    }

    [Fact]
    public async Task ListPlans_UnknownFamily404_IsCatalogUnavailable()
    {
        var h = new Harness();
        h.Maxio.Intercept = (_, _) => FakeMaxio.Json(HttpStatusCode.NotFound, "Not Found");

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().ListPlansAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(SubscriptionFailure.CatalogUnavailable, ex.Failure);
    }

    [Fact]
    public async Task ListPlans_ProviderRejectsCredentials_IsBillingMisconfigured()
    {
        var h = new Harness();
        h.Maxio.Intercept = (_, _) => FakeMaxio.Json(HttpStatusCode.Unauthorized, new { error = "bad key" });

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().ListPlansAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(SubscriptionFailure.BillingMisconfigured, ex.Failure);
        Assert.DoesNotContain("bad key", ex.Message);
    }

    [Fact]
    public async Task ListPlans_UnreadableBody_IsBadGateway()
    {
        var h = new Harness();
        h.Maxio.Intercept = (_, _) => FakeMaxio.Json(HttpStatusCode.OK, new { not = "a list" });

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().ListPlansAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(SubscriptionFailure.BadGateway, ex.Failure);
    }

    // ---- subscribe

    [Fact]
    public async Task Subscribe_NewUser_CreatesCustomerThenSubscriptionWithDeterministicReferences()
    {
        var h = new Harness();

        var result = await h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal("fake-pro", result.Subscription!.PlanHandle);
        Assert.Equal(9900, result.Subscription.PriceInCents);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingAt);

        var customerPost = h.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery.StartsWith("/customers.json"));
        Assert.Contains($"\"reference\":\"eshop-user:{UserKey}\"", customerPost.Body);
        Assert.Contains($"\"email\":\"{UserKey}\"", customerPost.Body);
        Assert.Contains("\"first_name\":\"demo.user\"", customerPost.Body);
        var subPost = h.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery.StartsWith("/subscriptions.json"));
        Assert.Contains("\"product_handle\":\"fake-pro\"", subPost.Body);
        Assert.Contains($"\"reference\":\"eshop-sub:{UserKey}:fake-pro\"", subPost.Body);
        Assert.Contains($"\"customer_id\":{h.Maxio.Customers[$"eshop-user:{UserKey}"]}", subPost.Body);

        using var db = h.NewContext();
        var claim = await db.SubscriptionClaims.SingleAsync();
        Assert.Equal($"{UserKey}|fake-pro", claim.ClaimKey);
        Assert.Equal(ClaimStatus.Completed, claim.Status);
        Assert.Equal(h.Maxio.Subscriptions.Single().Id, claim.MaxioSubscriptionId);
    }

    [Fact]
    public async Task Subscribe_Twice_SecondCallIsAReplayWithoutAnotherProviderWrite()
    {
        var h = new Harness();

        var first = await h.NewService().SubscribeAsync(User, "fake-basic", Guid.NewGuid(), CancellationToken.None);
        var second = await h.NewService().SubscribeAsync(User, "fake-basic", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Created, first.Outcome);
        Assert.Equal(SubscribeOutcome.Existing, second.Outcome);
        Assert.Equal(first.Subscription!.SubscriptionId, second.Subscription!.SubscriptionId);
        Assert.Single(h.Maxio.Subscriptions);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task Subscribe_ClaimLostOnRestart_ProviderPreCheckPreventsDuplicate()
    {
        var h = new Harness();
        await h.NewService().SubscribeAsync(User, "fake-basic", Guid.NewGuid(), CancellationToken.None);

        using (var db = h.NewContext())       // simulate the in-memory store being lost
        {
            db.SubscriptionClaims.RemoveRange(db.SubscriptionClaims);
            db.CustomerLinks.RemoveRange(db.CustomerLinks);
            await db.SaveChangesAsync();
        }

        var again = await h.NewService().SubscribeAsync(User, "fake-basic", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Existing, again.Outcome);
        Assert.Single(h.Maxio.Subscriptions);
        Assert.Single(h.Maxio.Customers);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_ConcurrentSecondRequest_IsRejectedByTheClaimPrimaryKey()
    {
        var h = new Harness();
        using var db1 = h.NewContext();
        db1.SubscriptionClaims.Add(new SubscriptionClaim { ClaimKey = $"{UserKey}|fake-pro", Status = ClaimStatus.Pending, ClaimedAt = h.Clock.Now });
        await db1.SaveChangesAsync();

        // a different DbContext (= a different request) meets the existing claim
        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(SubscriptionFailure.InProgress, ex.Failure);
        Assert.NotNull(ex.RetryAfter);
        Assert.Equal(0, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, h.Maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task Subscribe_ParallelRequests_ProduceExactlyOneProviderSubscription()
    {
        var h = new Harness();

        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { return (await h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None)).Outcome.ToString(); }
            catch (SubscriptionException ex) { return ex.Failure.ToString(); }
        })).ToArray();
        var outcomes = await Task.WhenAll(tasks);

        Assert.Single(h.Maxio.Subscriptions);
        Assert.Single(h.Maxio.Customers);
        Assert.Equal(1, outcomes.Count(o => o == nameof(SubscribeOutcome.Created)));
        Assert.All(outcomes, o => Assert.Contains(o, new[] { "Created", "Existing", "InProgress" }));
    }

    [Fact]
    public async Task Subscribe_StalePendingClaim_IsReconciledAgainstTheProvider()
    {
        var h = new Harness();
        await h.NewService().SubscribeAsync(User, "fake-basic", Guid.NewGuid(), CancellationToken.None);
        using (var db = h.NewContext())      // the process died after the write: claim is Pending and old
        {
            var claim = await db.SubscriptionClaims.SingleAsync();
            claim.Status = ClaimStatus.Pending;
            claim.MaxioSubscriptionId = null;
            await db.SaveChangesAsync();
        }
        h.Clock.Now += TimeSpan.FromMinutes(10);

        var result = await h.NewService().SubscribeAsync(User, "fake-basic", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Existing, result.Outcome);
        Assert.Single(h.Maxio.Subscriptions);
        using var check = h.NewContext();
        Assert.Equal(ClaimStatus.Completed, (await check.SubscriptionClaims.SingleAsync()).Status);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_IsRejectedBeforeAnyClaimOrWrite()
    {
        var h = new Harness();

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(User, "not-offered", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(SubscriptionFailure.UnknownPlan, ex.Failure);
        Assert.Equal(0, h.Maxio.Requests.Count(r => r.Method == HttpMethod.Post));
        using var db = h.NewContext();
        Assert.Empty(db.SubscriptionClaims);
    }

    [Fact]
    public async Task Subscribe_ArchivedPlan_IsNotOffered()
    {
        var h = new Harness();
        h.Maxio.Plans.Add(new("retired", "Retired", 1, Archived: true));

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(User, "retired", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(SubscriptionFailure.UnknownPlan, ex.Failure);
    }

    [Fact]
    public async Task Subscribe_PartialPlanListWithMissingHandle_Is503NotUnknownPlan()
    {
        var h = new Harness();
        h.Maxio.Plans.Clear();
        for (var i = 0; i < 1000; i++) h.Maxio.Plans.Add(new($"p{i}", $"P{i}", 1));

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(User, "p-late", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(SubscriptionFailure.Unavailable, ex.Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Subscribe_BlankPlanHandle_IsInvalidPlan(string? handle)
    {
        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => new Harness().NewService().SubscribeAsync(User, handle, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(SubscriptionFailure.InvalidPlan, ex.Failure);
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("@example.com")]
    [InlineData("a b@example.com")]
    public async Task Subscribe_UserNameNotUsableAsEmail_IsInvalidIdentity(string userName)
    {
        var h = new Harness();
        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(userName, "fake-pro", Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(SubscriptionFailure.InvalidIdentity, ex.Failure);
        Assert.Empty(h.Maxio.Requests);
    }

    [Fact]
    public async Task Subscribe_ProviderRejectsWith422_ReleasesTheClaimAndSurfacesSanitizedMessage()
    {
        var h = new Harness();
        h.Maxio.Intercept = (req, _) => req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")
            ? FakeMaxio.Json((HttpStatusCode)422, new { errors = new[] { "No payment method was on file" } })
            : null;

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(SubscriptionFailure.ProviderRejected, ex.Failure);
        Assert.Contains("No payment method", ex.Message);
        using var db = h.NewContext();
        Assert.Empty(db.SubscriptionClaims);                // released: a later retry can succeed
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));   // never resent by the SDK
    }

    [Fact]
    public async Task Subscribe_CreateSubscriptionConnectionFails_ReconcilesAndFindsTheWriteThatLanded()
    {
        var h = new Harness();
        h.Maxio.Intercept = (req, _) =>
        {
            if (req.Method != HttpMethod.Post || !req.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")) return null;
            // the provider processes the write, then the connection drops
            var sub = new FakeMaxio.FakeSubscription(5001, h.Maxio.Customers.Values.Single(), "fake-pro", $"eshop-sub:{UserKey}:fake-pro");
            h.Maxio.Subscriptions.Add(sub);
            throw new HttpRequestException("connection reset");
        };

        var result = await h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(5001, result.Subscription!.SubscriptionId);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        using var db = h.NewContext();
        var claim = await db.SubscriptionClaims.SingleAsync();
        Assert.Equal(ClaimStatus.Completed, claim.Status);
        Assert.Equal(5001, claim.MaxioSubscriptionId);
    }

    [Fact]
    public async Task Subscribe_CreateSubscriptionConnectionFails_NothingFound_RecordsUnknownAndNeverResends()
    {
        var h = new Harness();
        h.Maxio.Intercept = (req, _) =>
            req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")
                ? throw new HttpRequestException("connection reset")
                : null;

        var result = await h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Unknown, result.Outcome);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        using (var db = h.NewContext())
            Assert.Equal(ClaimStatus.Unknown, (await db.SubscriptionClaims.SingleAsync()).Status);

        // The next request settles it from the provider; while it is still unknown it does not write again.
        var again = await h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(SubscribeOutcome.Unknown, again.Outcome);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_CreateSubscriptionUnreadable2xx_IsTreatedAsUnknownAndReconciled()
    {
        var h = new Harness();
        h.Maxio.Intercept = (req, _) =>
        {
            if (req.Method != HttpMethod.Post || !req.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")) return null;
            h.Maxio.Subscriptions.Add(new FakeMaxio.FakeSubscription(5002, h.Maxio.Customers.Values.Single(), "fake-basic", $"eshop-sub:{UserKey}:fake-basic"));
            return FakeMaxio.Json(HttpStatusCode.Created, new { subscription = "not-an-object" });
        };

        var result = await h.NewService().SubscribeAsync(User, "fake-basic", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(5002, result.Subscription!.SubscriptionId);
    }

    [Fact]
    public async Task Subscribe_CreateSubscription503_NothingFound_ReleasesClaimAndReports503()
    {
        var h = new Harness();
        h.Maxio.Intercept = (req, _) =>
            req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")
                ? FakeMaxio.Json(HttpStatusCode.ServiceUnavailable, new { error = "down" })
                : null;

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(SubscriptionFailure.Unavailable, ex.Failure);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        using var db = h.NewContext();
        Assert.Empty(db.SubscriptionClaims);
    }

    [Fact]
    public async Task Subscribe_CreateCustomerConnectionFails_ReconcilesByReferenceAndContinues()
    {
        var h = new Harness();
        h.Maxio.Intercept = (req, _) =>
        {
            if (req.Method != HttpMethod.Post || !req.RequestUri!.AbsolutePath.EndsWith("/customers.json")) return null;
            h.Maxio.Customers[$"eshop-user:{UserKey}"] = 777;   // the customer was created, the answer was lost
            h.Maxio.Intercept = null;
            throw new HttpRequestException("connection reset");
        };

        var result = await h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(777, h.Maxio.Subscriptions.Single().CustomerId);
        using var db = h.NewContext();
        Assert.Equal(777, (await db.CustomerLinks.SingleAsync()).MaxioCustomerId);
    }

    [Fact]
    public async Task Subscribe_CreateCustomerConnectionFails_NothingFound_RecordsUnknownAndSkipsTheSubscriptionWrite()
    {
        var h = new Harness();
        h.Maxio.Intercept = (req, _) =>
            req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/customers.json")
                ? throw new HttpRequestException("connection reset")
                : null;

        var result = await h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Unknown, result.Outcome);
        Assert.Equal(0, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        using var db = h.NewContext();
        Assert.Equal(ClaimStatus.Unknown, (await db.CustomerLinks.SingleAsync()).Status);
        Assert.Empty(db.SubscriptionClaims);               // subscription claim freed: no subscription write was attempted
    }

    [Fact]
    public async Task Subscribe_DifferentPlansForSameNewUser_ShareOneCustomer()
    {
        var h = new Harness();

        await h.NewService().SubscribeAsync(User, "fake-basic", Guid.NewGuid(), CancellationToken.None);
        await h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None);

        Assert.Single(h.Maxio.Customers);
        Assert.Equal(2, h.Maxio.Subscriptions.Count);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    // ---- my subscriptions

    [Fact]
    public async Task ListMine_UserWithoutCustomer_IsEmpty()
    {
        var h = new Harness();

        var mine = await h.NewService().ListMineAsync(User, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(mine);
        Assert.DoesNotContain(h.Maxio.Requests, r => r.PathAndQuery.Contains("/subscriptions.json"));
    }

    [Fact]
    public async Task ListMine_ReturnsOnlyTheTokenUsersSubscriptions()
    {
        var h = new Harness();
        await h.NewService().SubscribeAsync(User, "fake-pro", Guid.NewGuid(), CancellationToken.None);
        await h.NewService().SubscribeAsync("other@example.com", "fake-basic", Guid.NewGuid(), CancellationToken.None);

        var mine = await h.NewService().ListMineAsync(User, Guid.NewGuid(), CancellationToken.None);
        var theirs = await h.NewService().ListMineAsync("OTHER@example.com", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("fake-pro", Assert.Single(mine).PlanHandle);
        Assert.Equal("fake-basic", Assert.Single(theirs).PlanHandle);
    }

    [Fact]
    public async Task ListMine_ProviderTimeout_IsReportedAsTimeout()
    {
        var h = new Harness();
        h.Maxio.Customers[$"eshop-user:{UserKey}"] = 1;
        h.Maxio.Intercept = (_, _) => throw new TaskCanceledException("timeout", new TimeoutException());

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => h.NewService().ListMineAsync(User, Guid.NewGuid(), CancellationToken.None));

        Assert.True(ex is SubscriptionException { Failure: SubscriptionFailure.Timeout or SubscriptionFailure.BadGateway });
    }

    // ---- options / configuration

    [Fact]
    public void Validator_RequiresApiKeyFamilyAndSubdomainUnlessBaseUrlIsSet()
    {
        var v = new MaxioOptionsValidator();

        Assert.True(v.Validate(null, new MaxioOptions { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "f" }).Succeeded);
        Assert.True(v.Validate(null, new MaxioOptions { ApiKey = "k", BaseUrl = "https://mock.test", ProductFamilyHandle = "f" }).Succeeded);

        foreach (var bad in new[]
        {
            new MaxioOptions { Subdomain = "s", ProductFamilyHandle = "f" },
            new MaxioOptions { ApiKey = "  ", Subdomain = "s", ProductFamilyHandle = "f" },
            new MaxioOptions { ApiKey = "k", ProductFamilyHandle = "f" },
            new MaxioOptions { ApiKey = "k", Subdomain = "s" },
            new MaxioOptions { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "f", BaseUrl = "not a url" },
            new MaxioOptions { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "f", BaseUrl = "ftp://x" },
        })
        {
            var result = v.Validate(null, bad);
            Assert.True(result.Failed);
            Assert.DoesNotContain("not a url", string.Join(" ", result.Failures!));   // names keys, never echoes values
        }
    }

    [Fact]
    public void EnvironmentVariableMapping_IsTheLowestPrecedenceSource()
    {
        var names = new[] { "MAXIO_API_KEY", "MAXIO_SITE_SUBDOMAIN", "MAXIO_DEFAULT_PRODUCT_FAMILY" };
        var saved = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable("MAXIO_API_KEY", "from-env");
            Environment.SetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN", "env-site");
            Environment.SetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY", "env-family");

            var builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder();
            builder.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?> { ["Maxio:Subdomain"] = "secret-site" });
            builder.AddMaxioEnvironmentVariableMapping();
            var config = builder.Build();

            Assert.Equal("from-env", config["Maxio:ApiKey"]);
            Assert.Equal("env-family", config["Maxio:ProductFamilyHandle"]);
            Assert.Equal("secret-site", config["Maxio:Subdomain"]);   // an explicit source wins over the mapped env name
        }
        finally
        {
            for (var i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], saved[i]);
        }
    }

    [Fact]
    public async Task BaseUrlOverride_IsUsedVerbatimAsTheRequestHost()
    {
        var h = new Harness();
        await h.NewService().ListPlansAsync(Guid.NewGuid(), CancellationToken.None);
        // Harness configures BaseUrl = https://maxio.test; the fake sees only paths, so assert through a host-capturing intercept.
        string? host = null;
        h.Maxio.Intercept = (req, _) => { host = req.RequestUri!.Host; return null; };
        await h.NewService().ListPlansAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("maxio.test", host);
    }
}
