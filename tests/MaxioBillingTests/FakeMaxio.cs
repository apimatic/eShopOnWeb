using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// A scripted Maxio site behind <see cref="StubHandler"/>. Each route defaults to the happy path; a test
/// replaces the routes it is about.
/// </summary>
public sealed class FakeMaxio
{
    public Func<CapturedRequest, Task<HttpResponseMessage>> Products { get; set; } =
        _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, Wire.StandardPlans()));

    public Func<CapturedRequest, Task<HttpResponseMessage>> Families { get; set; } =
        _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, "[]"));

    public Func<CapturedRequest, Task<HttpResponseMessage>> CustomerLookup { get; set; } =
        _ => Task.FromResult(StubHandler.Empty(HttpStatusCode.NotFound));

    public Func<CapturedRequest, Task<HttpResponseMessage>> CreateCustomer { get; set; } =
        _ => Task.FromResult(StubHandler.Json(HttpStatusCode.Created, Wire.Customer(42)));

    public Func<CapturedRequest, Task<HttpResponseMessage>> SubscriptionLookup { get; set; } =
        _ => Task.FromResult(StubHandler.Empty(HttpStatusCode.NotFound));

    public Func<CapturedRequest, Task<HttpResponseMessage>> CreateSubscription { get; set; } =
        r => Task.FromResult(StubHandler.Json(HttpStatusCode.Created, Wire.Subscription(1001, SentSubscriptionReference(r))));

    public Func<CapturedRequest, Task<HttpResponseMessage>> CustomerSubscriptions { get; set; } =
        _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, "[]"));

    public StubHandler CreateHandler() => new(Dispatch);

    private Task<HttpResponseMessage> Dispatch(CapturedRequest r)
    {
        var path = r.Uri.AbsolutePath;
        if (r.Method == HttpMethod.Get && path.StartsWith("/product_families/") && path.EndsWith("/products.json")) return Products(r);
        if (r.Method == HttpMethod.Get && path == "/product_families.json") return Families(r);
        if (r.Method == HttpMethod.Get && path == "/customers/lookup.json") return CustomerLookup(r);
        if (r.Method == HttpMethod.Post && path == "/customers.json") return CreateCustomer(r);
        if (r.Method == HttpMethod.Get && path == "/subscriptions/lookup.json") return SubscriptionLookup(r);
        if (r.Method == HttpMethod.Post && path == "/subscriptions.json") return CreateSubscription(r);
        if (r.Method == HttpMethod.Get && path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json")) return CustomerSubscriptions(r);
        return Task.FromResult(StubHandler.Empty(HttpStatusCode.NotImplemented));
    }

    /// <summary>The subscription reference the integration sent in a CreateSubscription body.</summary>
    public static string SentSubscriptionReference(CapturedRequest r)
    {
        using var doc = JsonDocument.Parse(r.Body!);
        return doc.RootElement.GetProperty("subscription").GetProperty("reference").GetString()!;
    }

    /// <summary>A responder that answers each call with the next response of the sequence (the last one repeats).</summary>
    public static Func<CapturedRequest, Task<HttpResponseMessage>> Sequence(params Func<CapturedRequest, HttpResponseMessage>[] responses)
    {
        var index = 0;
        return r =>
        {
            var i = Math.Min(Interlocked.Increment(ref index) - 1, responses.Length - 1);
            return Task.FromResult(responses[i](r));
        };
    }

    public static Func<CapturedRequest, Task<HttpResponseMessage>> ConnectionReset =>
        _ => throw new HttpRequestException("connection reset by peer");
}
