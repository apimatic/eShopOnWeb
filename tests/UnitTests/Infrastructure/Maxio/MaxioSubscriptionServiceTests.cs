using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string USER = "shopper@example.com";

    /// <summary>Answers each request from the first rule whose method and path fragment match; a rule may throw.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public record Rule(HttpMethod Method, string PathPart, Func<HttpResponseMessage> Respond);

        private readonly Queue<Rule> _rules = new();
        public List<(HttpMethod Method, string Path)> Requests { get; } = new();

        public StubHandler On(HttpMethod m, string pathPart, HttpStatusCode status, string json) =>
            On(m, pathPart, () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

        public StubHandler On(HttpMethod m, string pathPart, Func<HttpResponseMessage> respond)
        {
            _rules.Enqueue(new Rule(m, pathPart, respond));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            var rule = _rules.FirstOrDefault(r => r.Method == request.Method && request.RequestUri.AbsolutePath.Contains(r.PathPart))
                       ?? throw new InvalidOperationException($"Unexpected {request.Method} {request.RequestUri}");
            // consume exactly that rule
            var rest = _rules.Where(r => !ReferenceEquals(r, rule)).ToList();
            _rules.Clear();
            rest.ForEach(_rules.Enqueue);
            var response = rule.Respond();
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static (MaxioSubscriptionService Service, CatalogContext Db, StubHandler Handler) Build(Action<StubHandler> script)
    {
        var handler = new StubHandler();
        script(handler);

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Disabled()
        };
        options.Server.Production.Us.BaseUrl = "http://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);

        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = "fam"
        });

        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var users = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        users.FindByNameAsync(Arg.Any<string>()).Returns(Task.FromResult<ApplicationUser?>(null));

        var service = new MaxioSubscriptionService(client, new MaxioPlanCatalog(client, settings), settings, db, users,
            NullLogger<MaxioSubscriptionService>.Instance);
        return (service, db, handler);
    }

    private static StubHandler WithPlans(StubHandler h) => h
        .On(HttpMethod.Get, "/product_families.json", HttpStatusCode.OK, """[{"product_family":{"id":1,"handle":"fam"}}]""")
        .On(HttpMethod.Get, "/product_families/1/products.json", HttpStatusCode.OK, """[{"product":{"handle":"pro","name":"Pro","price_in_cents":100}}]""");

    private const string CUSTOMER_JSON = """{"customer":{"id":7}}""";
    private const string SUBS_JSON = """[{"subscription":{"id":9,"state":"active","product":{"handle":"pro","name":"Pro"}}}]""";

    [Fact]
    public async Task Subscribe_RejectsPlanOutsideTheFamilyListing_BeforeAnyWrite()
    {
        var (service, _, handler) = Build(h => WithPlans(h));

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync(USER, "not-offered", default));

        Assert.Equal(400, ex.StatusCode);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_HappyPath_CreatesCustomerThenSubscriptionAndRecordsClaims()
    {
        var (service, db, handler) = Build(h => WithPlans(h)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, """{"errors":["nope"]}""")
            .On(HttpMethod.Post, "/customers.json", HttpStatusCode.Created, CUSTOMER_JSON)
            .On(HttpMethod.Get, "/customers/7/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, """{"subscription":{"id":9,"state":"active","product":{"handle":"pro"}}}"""));

        var result = await service.SubscribeAsync(USER, "pro", default);

        Assert.Equal(9, result.SubscriptionId);
        Assert.Equal(ClaimStatus.Completed, (await db.MaxioCustomerClaims.SingleAsync()).Status);
        Assert.Equal(ClaimStatus.Completed, (await db.MaxioSubscriptionClaims.SingleAsync()).Status);
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Subscribe_WhenPendingClaimHeldByAnotherRequest_IsRejectedWithoutCallingMaxioWrites()
    {
        var (service, db, handler) = Build(h => WithPlans(h)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, """{"errors":["nope"]}"""));
        db.MaxioCustomerClaims.Add(new MaxioCustomerClaim { UserKey = USER, Status = ClaimStatus.Pending });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DuplicateException>(() => service.SubscribeAsync(USER, "pro", default));

        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_CustomerCreateConnectionFailure_IsSettledByReReadingByReference()
    {
        var (service, db, handler) = Build(h => WithPlans(h)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, """{"errors":["nope"]}""")
            .On(HttpMethod.Post, "/customers.json", () => throw new HttpRequestException("connection reset"))
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, CUSTOMER_JSON)
            .On(HttpMethod.Get, "/customers/7/subscriptions.json", HttpStatusCode.OK, SUBS_JSON));

        var result = await service.SubscribeAsync(USER, "pro", default);

        Assert.Equal(9, result.SubscriptionId);
        Assert.Equal(7, (await db.MaxioCustomerClaims.SingleAsync()).MaxioCustomerId);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post)); // no second create
    }

    [Fact]
    public async Task Subscribe_SubscriptionCreateConnectionFailure_FoundOnReReadIsReportedAndNotResent()
    {
        var (service, db, handler) = Build(h => WithPlans(h)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, CUSTOMER_JSON)
            .On(HttpMethod.Get, "/customers/7/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", () => throw new HttpRequestException("connection reset"))
            .On(HttpMethod.Get, "/customers/7/subscriptions.json", HttpStatusCode.OK, SUBS_JSON));

        var result = await service.SubscribeAsync(USER, "pro", default);

        Assert.Equal(9, result.SubscriptionId);
        Assert.Equal(ClaimStatus.Completed, (await db.MaxioSubscriptionClaims.SingleAsync()).Status);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Subscribe_SubscriptionCreateConnectionFailure_NothingFoundReleasesClaimAndIsRetryable()
    {
        var (service, db, _) = Build(h => WithPlans(h)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, CUSTOMER_JSON)
            .On(HttpMethod.Get, "/customers/7/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", () => throw new HttpRequestException("connection reset"))
            .On(HttpMethod.Get, "/customers/7/subscriptions.json", HttpStatusCode.OK, "[]"));

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync(USER, "pro", default));

        Assert.Equal(503, ex.StatusCode);
        Assert.Empty(db.MaxioSubscriptionClaims);
    }

    [Fact]
    public async Task Subscribe_ProviderRefusal_ReleasesClaimAndSurfaces422()
    {
        var (service, db, _) = Build(h => WithPlans(h)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, CUSTOMER_JSON)
            .On(HttpMethod.Get, "/customers/7/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method"]}"""));

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync(USER, "pro", default));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("No payment method", ex.Message);
        Assert.Empty(db.MaxioSubscriptionClaims);
    }
}
