using System.Net;
using System.Text.Json;
using static Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.MaxioTestHost;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// A stateful stand-in for the Maxio endpoints the billing service uses, served through
/// <see cref="StubHandler"/>. Tests change its state or swap a behaviour to drive each path.
/// </summary>
public sealed class FakeMaxio
{
    public const int CustomerId = 42;

    private readonly object _gate = new();
    private readonly List<(int Id, string Handle, string? Reference, string State)> _subscriptions = new();
    private int _nextSubscriptionId = 9001;

    public FakeMaxio(string userName)
    {
        UserName = userName;
        Handler = new StubHandler(Respond);
        CreateSubscription = body => StubHandler.Json(HttpStatusCode.Created, AddFromCreateBody(body));
    }

    public string UserName { get; }

    public StubHandler Handler { get; }

    public string ProductsJson { get; set; } = Products(Product("eshop-pro", 29900), Product("basic-plan", 2900));

    public bool CustomerExists { get; set; } = true;

    /// <summary>Behaviour of POST /subscriptions.json; receives the request body.</summary>
    public Func<string?, HttpResponseMessage> CreateSubscription { get; set; }

    /// <summary>When set, answers GET /subscriptions/lookup.json instead of the stored subscriptions.</summary>
    public Func<HttpResponseMessage>? FindSubscriptionOverride { get; set; }

    public int AddSubscription(string handle, string? reference, string state = "active")
    {
        lock (_gate)
        {
            var id = _nextSubscriptionId++;
            _subscriptions.Add((id, handle, reference, state));
            return id;
        }
    }

    /// <summary>Records the subscription a create body asks for (the create "landed") and returns its JSON.</summary>
    public string AddFromCreateBody(string? body)
    {
        using var doc = JsonDocument.Parse(body!);
        var subscription = doc.RootElement.GetProperty("subscription");
        var handle = subscription.GetProperty("product_handle").GetString()!;
        var reference = subscription.TryGetProperty("reference", out var r) ? r.GetString() : null;
        var id = AddSubscription(handle, reference);
        return Subscription(id, handle, reference);
    }

    public HttpResponseMessage Respond(HttpRequestMessage request, string? body)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        var query = Uri.UnescapeDataString(request.RequestUri.Query);
        switch (request.Method.Method, path)
        {
            case ("GET", ProductsPath):
                return StubHandler.Json(HttpStatusCode.OK, ProductsJson);
            case ("GET", CustomerLookupPath):
                return CustomerExists
                    ? StubHandler.Json(HttpStatusCode.OK, Customer(CustomerId, UserName))
                    : StubHandler.Json(HttpStatusCode.NotFound, """{"errors":["Not Found"]}""");
            case ("POST", CustomersPath):
                CustomerExists = true;
                return StubHandler.Json(HttpStatusCode.Created, Customer(CustomerId, UserName));
            case ("POST", SubscriptionsPath):
                return CreateSubscription(body);
            case ("GET", SubscriptionLookupPath):
            {
                if (FindSubscriptionOverride is not null)
                {
                    return FindSubscriptionOverride();
                }

                lock (_gate)
                {
                    var match = _subscriptions.FirstOrDefault(s => s.Reference is not null && query.Contains("reference=" + s.Reference));
                    return match.Handle is null
                        ? StubHandler.Empty(HttpStatusCode.NotFound)
                        : StubHandler.Json(HttpStatusCode.OK, Subscription(match.Id, match.Handle, match.Reference, match.State));
                }
            }
            case ("GET", "/customers/42/subscriptions.json"):
                lock (_gate)
                {
                    return StubHandler.Json(HttpStatusCode.OK,
                        "[" + string.Join(",", _subscriptions.Select(s => Subscription(s.Id, s.Handle, s.Reference, s.State))) + "]");
                }
            default:
                return StubHandler.Empty(HttpStatusCode.InternalServerError);
        }
    }
}
