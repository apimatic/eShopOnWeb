using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioClientContractTests
{
    [TestMethod]
    public async Task UsesSpecServerOverrideBasicAuthPaginationAndProductEnvelope()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                [{"product":{"id":42,"name":"Basic","handle":"basic-plan","description":"Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":7,"name":"Plans","handle":"test-family"}}}]
                """, Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var products = await client.ListProductsAsync(CancellationToken.None);

        Assert.AreEqual(1, products.Count);
        Assert.AreEqual("basic-plan", products[0].Handle);
        Assert.AreEqual(2900L, products[0].PriceInCents);
        Assert.AreEqual(
            "https://maxio.example.test/custom/products.json?page=1&per_page=200&include_archived=false",
            handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.AreEqual("Basic", handler.Requests[0].Headers.Authorization!.Scheme);
        Assert.AreEqual("contract-test-key:x", Encoding.UTF8.GetString(
            Convert.FromBase64String(handler.Requests[0].Headers.Authorization!.Parameter!)));
    }

    [TestMethod]
    public async Task SendsSpecCreateCustomerEnvelopeAndFieldNames()
    {
        var handler = new RecordingHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"customer":{"id":99,"first_name":"demo","last_name":"Customer","email":"demo@example.com","reference":"eshop-user-1"}}
                """, Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var customer = await client.CreateCustomerAsync(new MaxioCreateCustomer
        {
            FirstName = "demo",
            LastName = "Customer",
            Email = "demo@example.com",
            Reference = "eshop-user-1"
        }, CancellationToken.None);

        Assert.AreEqual(99L, customer.Id);
        StringAssert.Contains(handler.Requests[0].Body!, "\"customer\"");
        StringAssert.Contains(handler.Requests[0].Body!, "\"first_name\":\"demo\"");
        StringAssert.Contains(handler.Requests[0].Body!, "\"reference\":\"eshop-user-1\"");
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new MaxioOptions
        {
            ApiKey = "contract-test-key",
            Subdomain = "unused",
            ProductFamilyHandle = "test-family",
            BaseUrl = "https://maxio.example.test/custom"
        }));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<RecordedRequest, HttpResponseMessage> _response;

        public RecordingHandler(Func<RecordedRequest, HttpResponseMessage> response) => _response = response;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var recorded = new RecordedRequest(
                request.RequestUri,
                request.Headers,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(recorded);
            return _response(recorded);
        }
    }

    private sealed record RecordedRequest(
        Uri? RequestUri,
        System.Net.Http.Headers.HttpRequestHeaders Headers,
        string? Body);
}
