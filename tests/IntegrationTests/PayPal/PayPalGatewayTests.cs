using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using Microsoft.Extensions.Options;
using NSubstitute;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.PayPal;

/// <summary>
/// Tests the PayPal gateway through the SDK's HttpClient seam — a stub handler replaces the network,
/// so no real PayPal calls happen. The stub answers the OAuth token request and then the operation.
/// </summary>
public class PayPalGatewayTests
{
    private const string TokenJson =
        "{\"access_token\":\"stub-token\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"app_id\":\"app\"}";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _operationResponder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> operationResponder) =>
            _operationResponder = operationResponder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            HttpResponseMessage response;
            if (request.RequestUri!.AbsolutePath.Contains("oauth2/token"))
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(TokenJson, Encoding.UTF8, "application/json")
                };
            }
            else
            {
                response = _operationResponder(request);
            }
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static PayPalGateway CreateGateway(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var httpClient = new HttpClient(handler);
        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" }
        };
        var client = new PayPalServerSdkClient(httpClient, options);
        var settings = Options.Create(new PayPalOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Environment = "sandbox",
            Currency = "USD",
            TimeoutSeconds = 30,
            MaxReconciliationPages = 10
        });
        return new PayPalGateway(client, settings, Substitute.For<IAppLogger<PayPalGateway>>());
    }

    [Fact]
    public async Task VaultCard_maps_the_safe_descriptor_from_the_response()
    {
        const string vaultJson =
            "{\"id\":\"vault-123\",\"customer\":{\"id\":\"cust-1\"}," +
            "\"payment_source\":{\"card\":{\"last_digits\":\"1111\",\"brand\":\"VISA\"," +
            "\"expiry\":\"2030-01\",\"name\":\"Test Buyer\"}}}";
        var gateway = CreateGateway(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(vaultJson, Encoding.UTF8, "application/json")
        });

        var card = new CardDetails
        {
            Number = "4111111111111111",
            Expiry = "2030-01",
            SecurityCode = "123",
            CardholderName = "Test Buyer",
            BillingCountryCode = "US"
        };

        var result = await gateway.VaultCardAsync(card, customerId: null, "req-1", CancellationToken.None);

        Assert.Equal("vault-123", result.TokenId);
        Assert.Equal("cust-1", result.CustomerId);
        Assert.Equal("1111", result.LastDigits);
        Assert.Equal("VISA", result.Brand);
        Assert.Equal("2030-01", result.Expiry);
    }

    [Fact]
    public async Task Provider_error_is_translated_with_its_code()
    {
        const string errorJson =
            "{\"name\":\"UNPROCESSABLE_ENTITY\",\"message\":\"business validation failed\"," +
            "\"debug_id\":\"debug-xyz\",\"details\":[{\"issue\":\"INSTRUMENT_DECLINED\"}]}";
        var gateway = CreateGateway(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
        });

        var card = new CardDetails
        {
            Number = "4111111111111111",
            Expiry = "2030-01",
            SecurityCode = "123",
            BillingCountryCode = "US"
        };

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            gateway.VaultCardAsync(card, customerId: null, "req-2", CancellationToken.None));

        Assert.Equal("UNPROCESSABLE_ENTITY", ex.ProviderCode);
        Assert.Equal("debug-xyz", ex.ProviderDebugId);
    }
}
