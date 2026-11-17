using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Models.Enums;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class FinixGatewayConnectionFailureTests
{
    [TestMethod]
    public async Task AuthorizationConnectionFailureIsReportedAsUnknownOutcome()
    {
        var handler = new ThrowingHandler();
        var client = new FinixApiReferenceClient(
            new HttpClient(handler),
            new FinixApiReferenceClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = "test-user",
                    Password = "test-password"
                }
            });

        var gateway = new FinixGateway(
            client,
            new FinixOptions
            {
                MerchantId = "test-merchant",
                Currency = "USD"
            },
            Currency.Usd);

        var exception = await Assert.ThrowsExceptionAsync<PaymentProviderException>(
            () => gateway.AuthorizeAsync("PI_test", 100, "idem-test", null, default));

        Assert.IsTrue(exception.OutcomeUnknown);
        Assert.IsNull(exception.StatusCode);
        Assert.AreEqual(1, handler.Calls);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new HttpRequestException("connection reset");
        }
    }
}
