using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioSubscriptionServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public System.Collections.Generic.List<HttpRequestMessage> Requests { get; } = new();
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static (MaxioSubscriptionService Service, CatalogContext Db) Build(StubHandler handler)
    {
        var settings = new MaxioSettings { ApiKey = "test", Subdomain = "site", ProductFamilyHandle = "fam", BaseUrl = "https://maxio.test" };
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "x" },
            Retry = RetryOptions.Default() with { MaxRetries = 0 },
        };
        options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);

        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var users = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        users.FindByNameAsync("u@test.com").Returns(new ApplicationUser { UserName = "u@test.com", Email = "u@test.com" });

        return (new MaxioSubscriptionService(client, Options.Create(settings), db, users,
            NullLogger<MaxioSubscriptionService>.Instance), db);
    }

    private const string Products = """[{"product":{"handle":"p1","name":"Plan 1","price_in_cents":2900}}]""";
    private const string Customer = """{"customer":{"id":5,"reference":"u@test.com"}}""";

    [Fact]
    public async Task RejectsAPlanThatIsNotOffered_WithoutTouchingTheClaimStore()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.Contains("products") ? Json(HttpStatusCode.OK, Products) : Json(HttpStatusCode.NotFound, "{}"));
        var (service, db) = Build(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync("u@test.com", "other", default));

        Assert.Equal(400, ex.StatusCode);
        Assert.Empty(db.SubscriptionClaims);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ConnectionFailureOnCreateSubscription_SendsOnce_AndRecordsUnknownOutcome()
    {
        var handler = new StubHandler(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Post) throw new HttpRequestException("connection reset");
            if (path.Contains("products")) return Json(HttpStatusCode.OK, Products);
            if (path.Contains("customers")) return Json(HttpStatusCode.OK, Customer);
            return new HttpResponseMessage(HttpStatusCode.NotFound); // no subscription yet at Maxio
        });
        var (service, db) = Build(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync("u@test.com", "p1", default));

        Assert.Equal(503, ex.StatusCode);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post); // the SDK never resends a POST
        var claim = await db.SubscriptionClaims.SingleAsync();
        Assert.Equal(ClaimStatus.Unknown, claim.Status);
    }

    [Fact]
    public async Task ConnectionFailureOnCreateSubscription_ReconcilesWhenMaxioHoldsTheSubscription()
    {
        const string sub = """{"subscription":{"id":9,"state":"active","reference":"eshop:u@test.com:p1","product":{"handle":"p1","name":"Plan 1","price_in_cents":2900}}}""";
        var postSeen = false;
        var handler = new StubHandler(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Post) { postSeen = true; throw new HttpRequestException("connection reset"); }
            if (path.Contains("products")) return Json(HttpStatusCode.OK, Products);
            if (path.Contains("customers")) return Json(HttpStatusCode.OK, Customer);
            // the pre-create lookup misses; the post-failure lookup finds it
            return postSeen ? Json(HttpStatusCode.OK, sub) : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var (service, db) = Build(handler);

        var result = await service.SubscribeAsync("u@test.com", "p1", default);

        Assert.Equal(9, result.Id);
        Assert.Equal(ClaimStatus.Completed, (await db.SubscriptionClaims.SingleAsync()).Status);
    }

    [Fact]
    public void SettingsValidator_NamesTheMissingKeys_NotTheValues()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings { ApiKey = "secret-value" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:Subdomain"));
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:ProductFamilyHandle"));
        Assert.DoesNotContain(result.Failures!, f => f.Contains("secret-value"));
    }
}
