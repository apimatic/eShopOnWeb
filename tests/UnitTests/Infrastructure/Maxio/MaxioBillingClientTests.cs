using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingClientTests
{
    [Fact]
    public async Task ListProductsInFamilyMapsWrappedMaxioPayload()
    {
        const string json = """
            [
              {
                "product": {
                  "id": 1,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "description": "Pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month"
                }
              }
            ]
            """;

        var client = CreateClient(json);
        var plans = await client.ListProductsInFamilyAsync("eshop-subscribe");

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task FindCustomerByReferenceReturnsNullOn404()
    {
        var http = new HttpClient(new StubHandler(HttpStatusCode.NotFound, "{}"))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var client = new MaxioBillingClient(http, Substitute.For<ILogger<MaxioBillingClient>>());

        var customer = await client.FindCustomerByReferenceAsync("eshop:user-1");
        Assert.Null(customer);
    }

    private static MaxioBillingClient CreateClient(string json)
    {
        var http = new HttpClient(new StubHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        return new MaxioBillingClient(http, Substitute.For<ILogger<MaxioBillingClient>>());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;

        public StubHandler(HttpStatusCode status, string json)
        {
            _status = status;
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }
}
