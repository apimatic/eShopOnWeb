using System;
using System.Collections.Generic;
using System.Linq;
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

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioService maxioService, HttpContext httpContext) =>
            {
                return await HandleAsync(new EmptyRequest(), maxioService, httpContext);
            })
            .Produces<GetMySubscriptionsResponse>(StatusCodes.Status200OK)
            .Produces<object>(StatusCodes.Status401Unauthorized)
            .WithName("GetMySubscriptions")
            .RequireAuthorization()
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, IMaxioService maxioService, HttpContext httpContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var subscriptions = await maxioService.GetSubscriptionsForUserAsync(userId);

            var response = new GetMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(SubscriptionDto.FromMaxioSubscription).ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetMySubscriptionsResponse
{
    [JsonPropertyName("subscriptions")]
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
