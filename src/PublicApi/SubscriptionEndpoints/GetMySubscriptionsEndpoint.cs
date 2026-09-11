using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Get current user's subscriptions
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new GetMySubscriptionsRequest(), subscriptionService);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || !user.Identity?.IsAuthenticated == true)
            return Results.Unauthorized();

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? user.Identity?.Name;

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        var result = await subscriptionService.GetMySubscriptionsAsync(userId);

        if (!result.IsSuccess)
            return Results.BadRequest(new { error = result.ErrorMessage });

        response.Subscriptions = result.Subscriptions;
        return Results.Ok(response);
    }
}
