using System.Net;
using System.Text.Json;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Billing;

/// <summary>
/// A stateful fake of the Maxio endpoints the integration uses, behind <see cref="MaxioStubHandler"/>, plus a
/// factory for per-request service scopes that share one in-memory claim store (like concurrent HTTP requests).
/// No test reaches the network: the SDK client's HttpClient is backed by the stub.
/// </summary>
public sealed class FakeMaxio
{
    public const string Family = "eshop-subscribe";

    private readonly object _gate = new();
    private int _nextId = 1000;
    private readonly string _databaseName = "billing-" + Guid.NewGuid();
    private MaxioAdvancedBillingClient? _client;

    public MaxioStubHandler Stub { get; } = new();
    public Dictionary<string, int> Customers { get; } = new();
    public List<(int Id, int CustomerId, string? Reference, string PlanHandle, string State, DateTimeOffset CreatedAt)> Subscriptions { get; } = new();

    public MaxioSettings Settings { get; } = new()
    {
        ApiKey = "test-placeholder-key",
        BaseUrl = "https://maxio.test.invalid",
        ProductFamilyHandle = Family,
    };

    public MaxioCallPolicy Policy { get; set; } = new()
    {
        CallBudget = TimeSpan.FromSeconds(5),
        RequestBudget = TimeSpan.FromSeconds(20),
        AttemptTimeout = TimeSpan.FromSeconds(4),
        MaxRetries = 0,
        InFlightWindow = TimeSpan.FromSeconds(60),
    };

    public MutableClock Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>Optional hook run before a CreateSubscription is applied (e.g. to hold the first caller in flight).</summary>
    public Func<Task>? BeforeCreateSubscription { get; set; }

    /// <summary>Optional hook run before a CreateCustomer is applied.</summary>
    public Func<Task>? BeforeCreateCustomer { get; set; }

    public FakeMaxio()
    {
        Stub.OnJson(HttpMethod.Get, $"/product_families/handle:{Family}/products.json", HttpStatusCode.OK, PlansJson);
        Stub.OnSync(HttpMethod.Get, "/customers/lookup.json", (r, _) => LookupCustomer(QueryValue(r, "reference")));
        Stub.On(HttpMethod.Post, "/customers.json", async (_, body) =>
        {
            if (BeforeCreateCustomer is not null)
            {
                await BeforeCreateCustomer();
            }
            return CreateCustomerFrom(body);
        });
        Stub.On(HttpMethod.Post, "/subscriptions.json", async (_, body) =>
        {
            if (BeforeCreateSubscription is not null)
            {
                await BeforeCreateSubscription();
            }
            return CreateSubscriptionFrom(body);
        });
        Stub.OnSync(HttpMethod.Get, "/subscriptions/lookup.json", (r, _) => FindSubscription(QueryValue(r, "reference")));
        // /customers/{id}/subscriptions.json is matched by prefix below.
    }

    public const string PlansJson = """
        [
          {"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"handle":"eshop-subscribe"}}},
          {"product":{"id":2,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month","product_family":{"handle":"eshop-subscribe"}}}
        ]
        """;

    public MaxioAdvancedBillingClient Client
    {
        get
        {
            lock (_gate)
            {
                return _client ??= MaxioBillingServiceCollectionExtensions.CreateClient(
                    new HttpClient(new CustomerSubscriptionsRouter(this, Stub)), Settings, Policy, NullLoggerFactory.Instance);
            }
        }
    }

    /// <summary>A fresh request scope: own DbContext over the shared in-memory store, own gateway and service.</summary>
    public (SubscriptionService Service, CatalogContext Db) NewScope()
    {
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);
        var options = Options.Create(Settings);
        var gateway = new MaxioBillingGateway(Client, options, Policy, NullLogger<MaxioBillingGateway>.Instance);
        var service = new SubscriptionService(gateway, new BillingClaimStore(db), options, Policy, Clock, NullLogger<SubscriptionService>.Instance);
        return (service, db);
    }

    public static string QueryValue(HttpRequestMessage request, string key)
    {
        foreach (var pair in request.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == key)
            {
                return Uri.UnescapeDataString(parts[1].Replace('+', ' '));
            }
        }
        return string.Empty;
    }

    public HttpResponseMessage LookupCustomer(string reference)
    {
        lock (_gate)
        {
            return Customers.TryGetValue(reference, out var id)
                ? MaxioStubHandler.Json(HttpStatusCode.OK, CustomerJson(id, reference))
                : MaxioStubHandler.Json(HttpStatusCode.NotFound, "");
        }
    }

    public HttpResponseMessage CreateCustomerFrom(string? body)
    {
        using var doc = JsonDocument.Parse(body!);
        var reference = doc.RootElement.GetProperty("customer").GetProperty("reference").GetString()!;
        lock (_gate)
        {
            if (Customers.ContainsKey(reference))
            {
                return MaxioStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference must be unique"]}""");
            }
            var id = ++_nextId;
            Customers[reference] = id;
            return MaxioStubHandler.Json(HttpStatusCode.Created, CustomerJson(id, reference));
        }
    }

    public HttpResponseMessage CreateSubscriptionFrom(string? body)
    {
        using var doc = JsonDocument.Parse(body!);
        var s = doc.RootElement.GetProperty("subscription");
        var handle = s.GetProperty("product_handle").GetString()!;
        var customerId = s.GetProperty("customer_id").GetInt32();
        var reference = s.TryGetProperty("reference", out var r) ? r.GetString() : null;
        lock (_gate)
        {
            var id = ++_nextId;
            var created = (id, customerId, reference, handle, "active", DateTimeOffset.UtcNow);
            Subscriptions.Add(created);
            return MaxioStubHandler.Json(HttpStatusCode.Created, "{\"subscription\":" + SubscriptionJson(created) + "}");
        }
    }

    public HttpResponseMessage FindSubscription(string reference)
    {
        lock (_gate)
        {
            var match = Subscriptions.FirstOrDefault(s => s.Reference == reference);
            return match.Id == 0
                ? MaxioStubHandler.Json(HttpStatusCode.NotFound, "")
                : MaxioStubHandler.Json(HttpStatusCode.OK, "{\"subscription\":" + SubscriptionJson(match) + "}");
        }
    }

    public HttpResponseMessage ListCustomerSubscriptions(int customerId)
    {
        lock (_gate)
        {
            var items = Subscriptions.Where(s => s.CustomerId == customerId).Select(s => "{\"subscription\":" + SubscriptionJson(s) + "}");
            return MaxioStubHandler.Json(HttpStatusCode.OK, "[" + string.Join(",", items) + "]");
        }
    }

    public int AddCustomer(string reference)
    {
        lock (_gate)
        {
            var id = ++_nextId;
            Customers[reference] = id;
            return id;
        }
    }

    public int AddSubscription(int customerId, string? reference, string planHandle, string state = "active", DateTimeOffset? createdAt = null)
    {
        lock (_gate)
        {
            var id = ++_nextId;
            Subscriptions.Add((id, customerId, reference, planHandle, state, createdAt ?? DateTimeOffset.UtcNow));
            return id;
        }
    }

    private static string CustomerJson(int id, string reference) =>
        $$$"""{"customer":{"id":{{{id}}},"reference":"{{{reference}}}","email":"demouser@microsoft.com","first_name":"Demouser","last_name":"Customer"}}""";

    private static string SubscriptionJson((int Id, int CustomerId, string? Reference, string PlanHandle, string State, DateTimeOffset CreatedAt) s)
    {
        var price = s.PlanHandle == "eshop-pro" ? 29900 : 2900;
        var name = s.PlanHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan";
        var reference = s.Reference is null ? "null" : $"\"{s.Reference}\"";
        var created = s.CreatedAt.ToString("O");
        var next = s.CreatedAt.AddMonths(1).ToString("O");
        return $$$"""
            {"id":{{{s.Id}}},"state":"{{{s.State}}}","reference":{{{reference}}},"product_price_in_cents":{{{price}}},"currency":"USD",
             "current_period_ends_at":"{{{next}}}","next_assessment_at":"{{{next}}}","created_at":"{{{created}}}","activated_at":"{{{created}}}",
             "customer":{"id":{{{s.CustomerId}}}},
             "product":{"id":1,"handle":"{{{s.PlanHandle}}}","name":"{{{name}}}","price_in_cents":{{{price}}},"interval":1,"interval_unit":"month","product_family":{"handle":"eshop-subscribe"} } }
            """;
    }

    /// <summary>Routes GET /customers/{id}/subscriptions.json to the fake state; everything else to the stub.</summary>
    private sealed class CustomerSubscriptionsRouter : DelegatingHandler
    {
        private readonly FakeMaxio _fake;

        public CustomerSubscriptionsRouter(FakeMaxio fake, HttpMessageHandler inner) : base(inner) => _fake = fake;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (request.Method == HttpMethod.Get && segments.Length == 3 && segments[0] == "customers" && segments[2] == "subscriptions.json"
                && int.TryParse(segments[1], out var customerId))
            {
                _fake.Stub.Requests.Enqueue((request.Method, request.RequestUri.AbsolutePath, request.RequestUri.Query, null));
                var response = _fake.ListCustomerSubscriptions(customerId);
                response.RequestMessage = request;
                return Task.FromResult(response);
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}

/// <summary>A settable clock for claim ages (the SDK keeps the system clock).</summary>
public sealed class MutableClock : TimeProvider
{
    private DateTimeOffset _now;

    public MutableClock(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
