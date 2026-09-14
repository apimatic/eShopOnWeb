using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class MaxioApiClientTests
{
    private static (MaxioApiClient Client, List<HttpRequestMessage> Requests) BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new FuncHttpMessageHandler(request =>
        {
            requests.Add(request);
            return Task.FromResult(responder(request));
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://unused.invalid")
        };

        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "secret-key",
            Subdomain = "cp-exp-8",
            Environment = "US",
            ProductFamilyHandle = "eshop-subscribe"
        });

        var client = new MaxioApiClient(httpClient, options, NullLogger<MaxioApiClient>.Instance);
        return (client, requests);
    }

    [TestMethod]
    public async Task CreateCustomer_SerializesSnakeCaseBodyAndBasicAuth()
    {
        var (client, requests) = BuildClient(request =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""customer"":{""id"":123,""first_name"":""Jane"",""last_name"":""Doe"",""email"":""jane@example.com"",""reference"":""ref-1""}}", Encoding.UTF8, "application/json")
            };
        });

        var created = await client.CreateCustomerAsync(new MaxioCustomerCreateRequest
        {
            FirstName = "Jane",
            LastName = "Doe",
            Email = "jane@example.com",
            Reference = "ref-1"
        }, CancellationToken.None);

        Assert.AreEqual(123, created.Id);
        Assert.AreEqual("/customers.json", requests[0].RequestUri?.AbsolutePath);

        var expectedAuth = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("secret-key:X"));
        Assert.AreEqual(expectedAuth, requests[0].Headers.Authorization!.ToString());

        string body = await requests[0].Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement.GetProperty("customer");
        Assert.IsTrue(root.TryGetProperty("first_name", out _));
        Assert.IsTrue(root.TryGetProperty("last_name", out _));
        Assert.AreEqual("ref-1", root.GetProperty("reference").GetString());
        Assert.IsFalse(root.TryGetProperty("FirstName", out _));
    }

    [TestMethod]
    public async Task CreateSubscription_IncludesPaymentCollectionMethod()
    {
        var (client, requests) = BuildClient(request =>
        {
            string payload = @"{""subscription"":{""id"":77,""state"":""active"",""product_price_in_cents"":29900,""created_at"":""2026-01-01T00:00:00Z"",""next_assessment_at"":""2026-02-01T00:00:00Z"",""product"":{""name"":""Pro Plan"",""handle"":""eshop-pro"",""price_in_cents"":29900,""interval"":1,""interval_unit"":""month""}}}";
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        });

        var created = await client.CreateSubscriptionAsync(new MaxioSubscriptionCreateRequest
        {
            ProductHandle = "eshop-pro",
            CustomerId = 123,
            PaymentCollectionMethod = "remittance"
        }, CancellationToken.None);

        Assert.AreEqual(77, created.Id);
        Assert.AreEqual("active", created.State);
        Assert.AreEqual("eshop-pro", created.Product!.Handle);
        Assert.AreEqual(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), created.NextAssessmentAt);

        string body = await requests[0].Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var subscription = json.RootElement.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", subscription.GetProperty("product_handle").GetString());
        Assert.AreEqual(123, subscription.GetProperty("customer_id").GetInt32());
        Assert.AreEqual("remittance", subscription.GetProperty("payment_collection_method").GetString());
        Assert.IsFalse(subscription.TryGetProperty("productHandle", out _));
    }

    [TestMethod]
    public async Task FindCustomerByReference_Treats404AsNotFound()
    {
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(@"{""errors"":[""Not Found""]}", Encoding.UTF8, "application/json")
        });

        var result = await client.FindCustomerByReferenceAsync("nobody@example.com", CancellationToken.None);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task ListFamilyProducts_ParsesProductEnvelopes()
    {
        var (client, requests) = BuildClient(_ =>
        {
            string payload = @"[{""product"":{""id"":1,""name"":""Pro"",""handle"":""eshop-pro"",""price_in_cents"":29900,""interval"":1,""interval_unit"":""month""}},{""product"":{""id"":2,""name"":""Basic"",""handle"":""basic-plan"",""price_in_cents"":2900,""interval"":1,""interval_unit"":""month""}}]";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        });

        var products = await client.ListFamilyProductsAsync("eshop-subscribe", CancellationToken.None);

        Assert.AreEqual(2, products.Count);
        Assert.AreEqual("eshop-pro", products[0].Handle);
        Assert.AreEqual(29900, products[0].PriceInCents);
        Assert.AreEqual("/product_families/handle:eshop-subscribe/products.json", requests[0].RequestUri!.AbsolutePath);
    }

    private sealed class FuncHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public FuncHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return await _handler(request);
        }
    }
}
