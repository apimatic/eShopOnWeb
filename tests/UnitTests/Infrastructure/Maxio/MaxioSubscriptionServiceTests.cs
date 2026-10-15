using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string Plan = "plan-a";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public int Count(HttpMethod method, string pathPart) =>
            Requests.Count(r => r.Method == method && r.RequestUri!.AbsolutePath.Contains(pathPart));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string ProductJson = """{"handle":"plan-a","name":"Plan A","price_in_cents":1000,"interval":1,"interval_unit":"month"}""";
    private const string SubscriptionJson =
        """{"subscription":{"id":11,"state":"active","product_price_in_cents":1000,"next_assessment_at":"2026-11-01T00:00:00Z","product":""" + ProductJson + "}}";

    /// <summary>A fake Maxio: 'fail' decides how the subscription POST answers.</summary>
    private static HttpResponseMessage Maxio(HttpRequestMessage r, Func<HttpResponseMessage>? failSubscriptionPost = null)
    {
        var path = r.RequestUri!.AbsolutePath;
        if (path.Contains("/products.json")) return Json(HttpStatusCode.OK, $$"""[{"product":{{ProductJson}}}]""");
        if (path.Contains("/customers/lookup.json")) return Json(HttpStatusCode.NotFound, "{}");
        if (path.EndsWith("/customers.json")) return Json(HttpStatusCode.Created, """{"customer":{"id":7}}""");
        if (path.Contains("/subscriptions/lookup.json")) return Json(HttpStatusCode.NotFound, "{}");
        if (path.EndsWith("/subscriptions.json") && r.Method == HttpMethod.Post)
            return failSubscriptionPost?.Invoke() ?? Json(HttpStatusCode.Created, SubscriptionJson);
        if (path.Contains("/subscriptions/11.json")) return Json(HttpStatusCode.OK, SubscriptionJson);
        return Json(HttpStatusCode.NotFound, "{}");
    }

    private static UserManager<ApplicationUser> Users()
    {
        var users = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        users.FindByNameAsync("shopper@example.com").Returns(new ApplicationUser { Id = "user-1", UserName = "shopper@example.com", Email = "shopper@example.com" });
        return users;
    }

    private static CatalogContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(dbName).Options);

    private static MaxioSubscriptionService NewService(StubHandler handler, CatalogContext db, MaxioSettings? settings = null)
    {
        settings ??= new MaxioSettings { ApiKey = "k", Subdomain = "site", ProductFamilyHandle = "fam" };
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" },
            Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Disabled()
        };
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
        else options.Server.Production.Us.Site = settings.Subdomain!;
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        return new MaxioSubscriptionService(client, settings, db, Users(), NullLogger<MaxioSubscriptionService>.Instance);
    }

    [Fact]
    public async Task SecondSubscribeToSamePlanDoesNotCreateAnotherSubscription()
    {
        var handler = new StubHandler(r => Maxio(r));
        var dbName = Guid.NewGuid().ToString();

        var first = await NewService(handler, NewContext(dbName)).SubscribeAsync("shopper@example.com", Plan, default);
        var second = await NewService(handler, NewContext(dbName)).SubscribeAsync("shopper@example.com", Plan, default);

        Assert.Equal(SubscribeOutcome.Created, first.Outcome);
        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal("active", second.Subscription!.State);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task ConcurrentSubscribesReachMaxioOnce()
    {
        var handler = new StubHandler(r => Maxio(r));
        var dbName = Guid.NewGuid().ToString();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { return (await NewService(handler, NewContext(dbName)).SubscribeAsync("shopper@example.com", Plan, default)).Outcome.ToString(); }
            catch (SubscriptionException ex) { return ex.Failure.ToString(); }
        }));

        Assert.Equal(1, handler.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/customers.json"));
        Assert.Contains("Created", results);
    }

    [Fact]
    public async Task UnknownPlanIsRejectedBeforeAnyWrite()
    {
        var handler = new StubHandler(r => Maxio(r));
        var ex = await Assert.ThrowsAsync<SubscriptionException>(() =>
            NewService(handler, NewContext(Guid.NewGuid().ToString())).SubscribeAsync("shopper@example.com", "not-offered", default));

        Assert.Equal(SubscriptionFailure.InvalidPlan, ex.Failure);
        Assert.Equal(0, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ConnectionFailureOnCreateIsSettledByReferenceLookup()
    {
        var created = false;
        var handler = new StubHandler(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("/subscriptions.json") && r.Method == HttpMethod.Post) { created = true; throw new HttpRequestException("connection reset"); }
            if (path.Contains("/subscriptions/lookup.json") && created) return Json(HttpStatusCode.OK, SubscriptionJson); // the write had landed
            return Maxio(r);
        });
        var db = NewContext(Guid.NewGuid().ToString());

        var result = await NewService(handler, db).SubscribeAsync("shopper@example.com", Plan, default);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(11, result.Subscription!.SubscriptionId);
        Assert.Equal(SubscriptionLinkState.Active, (await db.MaxioSubscriptionLinks.SingleAsync()).State);
    }

    [Fact]
    public async Task ConnectionFailureWithFailedReReadLeavesUnknownClaim()
    {
        var created = false;
        var handler = new StubHandler(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("/subscriptions.json") && r.Method == HttpMethod.Post) { created = true; throw new HttpRequestException("connection reset"); }
            if (path.Contains("/subscriptions/lookup.json") && created) throw new HttpRequestException("still down");
            return Maxio(r);
        });
        var db = NewContext(Guid.NewGuid().ToString());

        var result = await NewService(handler, db).SubscribeAsync("shopper@example.com", Plan, default);

        Assert.Equal(SubscribeOutcome.Pending, result.Outcome);
        Assert.Equal(SubscriptionLinkState.Unknown, (await db.MaxioSubscriptionLinks.SingleAsync()).State);
    }

    [Fact]
    public async Task ProviderRejectionReleasesTheClaimAndReports422()
    {
        var handler = new StubHandler(r => Maxio(r, () => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["nope"]}""")));
        var db = NewContext(Guid.NewGuid().ToString());

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() =>
            NewService(handler, db).SubscribeAsync("shopper@example.com", Plan, default));

        Assert.Equal(SubscriptionFailure.Rejected, ex.Failure);
        Assert.Empty(await db.MaxioSubscriptionLinks.ToListAsync());
    }

    [Fact]
    public async Task BaseUrlOverrideIsUsedVerbatim()
    {
        var handler = new StubHandler(r => Maxio(r));
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "fam", BaseUrl = "http://maxio.test:8123/custom" };

        await NewService(handler, NewContext(Guid.NewGuid().ToString()), settings).ListPlansAsync(default);

        var uri = handler.Requests.Single().RequestUri!;
        Assert.Equal("maxio.test", uri.Host);
        Assert.Equal(8123, uri.Port);
        Assert.StartsWith("/custom/", uri.AbsolutePath);
        Assert.Contains("handle:fam", Uri.UnescapeDataString(uri.AbsolutePath));
    }

    [Theory]
    [InlineData(null, "s", "f", null, "Maxio:ApiKey")]
    [InlineData("  ", "s", "f", null, "Maxio:ApiKey")]
    [InlineData("k", null, "f", null, "Maxio:Subdomain")]
    [InlineData("k", "s", " ", null, "Maxio:ProductFamilyHandle")]
    [InlineData("k", null, "f", "not a url", "Maxio:BaseUrl")]
    public void SettingsValidationNamesTheOffendingKey(string? key, string? subdomain, string? family, string? baseUrl, string expectedKey)
    {
        var problem = new MaxioSettings { ApiKey = key, Subdomain = subdomain, ProductFamilyHandle = family, BaseUrl = baseUrl }.Validate();
        Assert.Contains(expectedKey, problem);
    }

    [Fact]
    public void BaseUrlReplacesSubdomainRequirement()
    {
        Assert.Null(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://example.test" }.Validate());
    }
}
