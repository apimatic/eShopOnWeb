using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.TestSupport;

public static class MaxioTestClientFactory
{
    public static (MaxioSubscriptionService Service, StubHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        int maxRetries = 0)
    {
        var handler = new StubHandler(responder);
        var httpClient = new HttpClient(handler);

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-api-key", Password = "x" },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = Math.Max(1, maxRetries),
                Delay = TimeSpan.FromMilliseconds(1),
                MaxJitter = TimeSpan.Zero
            }
        };
        options.Server.Production.Us.Site = "test-site";

        var client = new MaxioAdvancedBillingClient(httpClient, options);
        var settings = new MaxioSettings
        {
            ApiKey = "test-api-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        };

        var service = new MaxioSubscriptionService(client, settings, new MaxioSubscribeGate(), NullLogger<MaxioSubscriptionService>.Instance);
        return (service, handler);
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Serves each scripted response once, in order, regardless of which request it came from — the
    /// tests using this know their service's exact call sequence and script accordingly.
    /// </summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> Sequenced(params Func<HttpRequestMessage, HttpResponseMessage>[] steps)
    {
        var queue = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(steps);
        return request =>
        {
            if (queue.Count == 0)
            {
                throw new InvalidOperationException($"No more scripted responses; unexpected {request.Method} {request.RequestUri}.");
            }

            return queue.Dequeue()(request);
        };
    }

    public static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode status, string json) =>
        _ => JsonResponse(status, json);

    public static Func<HttpRequestMessage, HttpResponseMessage> Throw<TException>(Func<TException> factory) where TException : Exception =>
        _ => throw factory();
}
