using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, maxioService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<object>(StatusCodes.Status400BadRequest)
            .WithName("CreateSubscription")
            .RequireAuthorization()
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext httpContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
            var userFirstName = httpContext.User.FindFirst("first_name")?.Value ?? "User";
            var userLastName = httpContext.User.FindFirst("last_name")?.Value ?? "Account";

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return Results.BadRequest(new { error = "planHandle is required" });
            }

            var subscription = await maxioService.CreateSubscriptionAsync(
                userId, userEmail ?? $"user-{userId}@eshop.local", userFirstName, userLastName, request.PlanHandle);

            var response = new CreateSubscriptionResponse
            {
                Subscription = SubscriptionDto.FromMaxioSubscription(subscription)
            };

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (HttpRequestException ex)
        {
            return Results.BadRequest(new { error = $"Subscription creation failed: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest
{
    [JsonPropertyName("planHandle")]
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto Subscription { get; set; } = new();
}
