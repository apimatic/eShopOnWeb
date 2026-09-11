using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionRoutes
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "subscription-routes.log");

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}"); } catch { }
    }

    private static MaxioAdvancedBillingClient? _client;
    private static MaxioSubscriptionService? _service;
    private static readonly object _lock = new();

    private static MaxioSubscriptionService CreateService(string apiKey, string subdomain, string? baseUrl, string productFamily)
    {
        if (_service != null) return _service;
        lock (_lock)
        {
            if (_service != null) return _service;

            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = apiKey,
                    Password = "x"
                },
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            if (!string.IsNullOrEmpty(baseUrl))
            {
                options.Server = new ServerOptions
                {
                    Production = new ProductionOptions
                    {
                        Us = new ProductionOptions.UsOptions { BaseUrl = baseUrl }
                    }
                };
            }
            else
            {
                options.Server = new ServerOptions
                {
                    Production = new ProductionOptions
                    {
                        Us = new ProductionOptions.UsOptions { Site = subdomain }
                    }
                };
            }

            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
            _client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
            _service = new MaxioSubscriptionService(_client, productFamily);
            return _service;
        }
    }

    public static void MapSubscriptionRoutes(this WebApplication app)
    {
        Log("MapSubscriptionRoutes called");

        var maxioApiKey = app.Configuration["Maxio:ApiKey"]
            ?? Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        var maxioSubdomain = app.Configuration["Maxio:Subdomain"]
            ?? Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
        var maxioProductFamily = app.Configuration["Maxio:ProductFamilyHandle"]
            ?? Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY") ?? "eshop-subscribe";
        var maxioBaseUrl = app.Configuration["Maxio:BaseUrl"];

        Log($"Credentials present: apiKey={(!string.IsNullOrEmpty(maxioApiKey))}, subdomain={maxioSubdomain}, family={maxioProductFamily}");

        app.MapGet("/api/subscription-plans", async (HttpContext httpContext) =>
        {
            try
            {
                Log("subscription-plans called");
                var ct = httpContext.RequestAborted;
                var svc = CreateService(maxioApiKey!, maxioSubdomain!, maxioBaseUrl, maxioProductFamily);
                var plans = await svc.ListPlansAsync(ct);
                Log($"Got {plans.Count} plans");
                var response = new ListSubscriptionPlansResponse();
                response.Plans.AddRange(plans);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                Log($"subscription-plans error: {ex}");
                return Results.Problem($"Failed to list plans: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .Produces<ListSubscriptionPlansResponse>()
        .WithTags("SubscriptionEndpoints");

        app.MapPost("/api/subscriptions", async (CreateSubscriptionRequest request, HttpContext httpContext) =>
        {
            try
            {
                Log("subscriptions POST called");
                var ct = httpContext.RequestAborted;
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? httpContext.User.FindFirstValue("sub");
                var email = httpContext.User.FindFirstValue(ClaimTypes.Email)
                    ?? httpContext.User.FindFirstValue("email")
                    ?? $"{userId}@placeholder.local";
                var firstName = httpContext.User.FindFirstValue(ClaimTypes.GivenName)
                    ?? httpContext.User.FindFirstValue("given_name")
                    ?? "Unknown";
                var lastName = httpContext.User.FindFirstValue(ClaimTypes.Surname)
                    ?? httpContext.User.FindFirstValue("family_name")
                    ?? "User";

                if (string.IsNullOrEmpty(userId))
                    return Results.BadRequest("User ID not found in token.");

                var svc = CreateService(maxioApiKey!, maxioSubdomain!, maxioBaseUrl, maxioProductFamily);
                var subscription = await svc.SubscribeAsync(
                    userId, email, firstName, lastName, request.ProductHandle, ct);

                var response = new CreateSubscriptionResponse();
                response.Subscription = subscription;
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                Log($"subscriptions error: {ex}");
                return Results.Problem($"Failed to create subscription: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .Produces<CreateSubscriptionResponse>()
        .WithTags("SubscriptionEndpoints");

        app.MapGet("/api/my-subscriptions", async (HttpContext httpContext) =>
        {
            try
            {
                Log("my-subscriptions called");
                var ct = httpContext.RequestAborted;
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? httpContext.User.FindFirstValue("sub");
                var email = httpContext.User.FindFirstValue(ClaimTypes.Email)
                    ?? httpContext.User.FindFirstValue("email")
                    ?? $"{userId}@placeholder.local";
                var firstName = httpContext.User.FindFirstValue(ClaimTypes.GivenName)
                    ?? httpContext.User.FindFirstValue("given_name")
                    ?? "Unknown";
                var lastName = httpContext.User.FindFirstValue(ClaimTypes.Surname)
                    ?? httpContext.User.FindFirstValue("family_name")
                    ?? "User";

                if (string.IsNullOrEmpty(userId))
                    return Results.BadRequest("User ID not found in token.");

                var svc = CreateService(maxioApiKey!, maxioSubdomain!, maxioBaseUrl, maxioProductFamily);
                var subscriptions = await svc.ListMySubscriptionsAsync(
                    userId, email, firstName, lastName, ct);

                var response = new ListMySubscriptionsResponse();
                response.Subscriptions.AddRange(subscriptions);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                Log($"my-subscriptions error: {ex}");
                return Results.Problem($"Failed to list subscriptions: {ex.Message}", statusCode: 500);
            }
        })
        .RequireAuthorization()
        .Produces<ListMySubscriptionsResponse>()
        .WithTags("SubscriptionEndpoints");
    }
}
