using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Builds the billing service over a stubbed transport, so tests exercise the real SDK — serialization,
/// URL building, error translation — without a network.
/// </summary>
/// <remarks>
/// The routes below are the ones this integration was observed to call against a live Maxio site, so a
/// test failing to match a route means the request changed shape, which is exactly what we want to catch.
/// </remarks>
internal sealed class MaxioTestHarness
{
    public const string ProductFamilyHandle = "eshop-subscribe";
    public const int ProductFamilyId = 4242;
    public const int CustomerId = 98839482;
    public const string CustomerReference = "eshoponweb-demouser@microsoft.com";

    private readonly List<(HttpMethod Method, string Path, string Query, string Body)> _requests = new();
    private readonly List<Func<HttpRequestMessage, HttpResponseMessage?>> _routes = new();

    public MaxioSettings Settings { get; } = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = ProductFamilyHandle
    };

    public IReadOnlyList<(HttpMethod Method, string Path, string Query, string Body)> Requests => _requests;

    public int CountOf(HttpMethod method, string path) =>
        _requests.Count(request => IsMatch(request, method, path));

    public (HttpMethod Method, string Path, string Query, string Body) Last(HttpMethod method, string path)
    {
        var matches = _requests.Where(request => IsMatch(request, method, path)).ToList();

        return matches.Count == 0
            ? throw new InvalidOperationException($"No recorded {method} {path} request.")
            : matches[^1];
    }

    private static bool IsMatch(
        (HttpMethod Method, string Path, string Query, string Body) request,
        HttpMethod method,
        string path) =>
        request.Method == method && string.Equals(request.Path, path, StringComparison.OrdinalIgnoreCase);

    /// <summary>Answers a request matching <paramref name="method"/> and <paramref name="path"/>.</summary>
    public MaxioTestHarness Route(HttpMethod method, string path, HttpStatusCode status, string json)
    {
        _routes.Add(request => Matches(request, method, path)
            ? new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }
            : null);

        return this;
    }

    /// <summary>Answers a matching request differently on each call, so retries and races are observable.</summary>
    public MaxioTestHarness RouteSequence(
        HttpMethod method,
        string path,
        params (HttpStatusCode Status, string Json)[] responses)
    {
        var index = 0;

        _routes.Add(request =>
        {
            if (!Matches(request, method, path))
            {
                return null;
            }

            var (status, json) = responses[Math.Min(index, responses.Length - 1)];
            index++;

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        return this;
    }

    /// <summary>Fails a matching request at the transport, the trigger the SDK retries on every verb.</summary>
    public MaxioTestHarness RouteTransportFailure(HttpMethod method, string path)
    {
        _routes.Add(request => Matches(request, method, path)
            ? throw new HttpRequestException("connection reset")
            : null);

        return this;
    }

    public MaxioSubscriptionBillingService BuildService()
    {
        var client = BuildClient();
        var options = Options.Create(Settings);

        return new MaxioSubscriptionBillingService(
            client,
            new MaxioSiteProvider(client, options, NullLogger<MaxioSiteProvider>.Instance),
            new MaxioSubscribeGate(),
            options,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private MaxioAdvancedBillingClient BuildClient()
    {
        // The send guard sits in the pipeline exactly as the DI registration puts it, so tests observe the
        // same write-once behaviour production gets.
        var guard = new MaxioSingleSendGuardHandler { InnerHandler = new StubHandler(this) };

        return new MaxioAdvancedBillingClient(new HttpClient(guard), new MaxioAdvancedBillingClientOptions());
    }

    private static bool Matches(HttpRequestMessage request, HttpMethod method, string path) =>
        request.Method == method &&
        string.Equals(request.RequestUri?.AbsolutePath, path, StringComparison.OrdinalIgnoreCase);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly MaxioTestHarness _harness;

        public StubHandler(MaxioTestHarness harness) => _harness = harness;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            // Recorded before dispatch, so a request that then fails still counts as having been sent.
            _harness._requests.Add((
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.RequestUri?.Query ?? string.Empty,
                body));

            foreach (var route in _harness._routes)
            {
                var response = route(request);
                if (response is not null)
                {
                    return response;
                }
            }

            throw new InvalidOperationException(
                $"No stub route for {request.Method} {request.RequestUri?.AbsolutePath}");
        }
    }
}
