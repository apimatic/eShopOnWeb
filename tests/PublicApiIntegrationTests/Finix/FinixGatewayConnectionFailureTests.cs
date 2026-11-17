using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Servers;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Finix;

[TestClass]
public class FinixGatewayConnectionFailureTests
{
    [TestMethod]
    public async Task ConnectionFailureOnWriteIsReportedAsUnknownOutcome()
    {
        var handler = new ThrowingHandler();
        var client = new FinixApiReferenceClient(
            new HttpClient(handler),
            new FinixApiReferenceClientOptions
            {
                Environment = ServerEnvironment.Production,
                BasicAuth = new BasicAuthCredentials { Username = "test", Password = "test" }
            });
        var gateway = new FinixGateway(
            client,
            Options.Create(new FinixOptions
            {
                Username = "test",
                Password = "test",
                MerchantId = "test",
                Environment = "sandbox",
                Currency = "USD"
            }));

        var exception = await Assert.ThrowsExceptionAsync<FinixProviderException>(
            () => gateway.AuthorizeAsync(100, "PI-test", "idem-test", CancellationToken.None));

        Assert.IsTrue(exception.OutcomeUnknown);
        Assert.AreEqual("FINIX_CONNECTION_FAILED", exception.Code);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection reset");
    }
}
