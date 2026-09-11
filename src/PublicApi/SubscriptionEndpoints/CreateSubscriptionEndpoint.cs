using System;
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
/// Subscribe to a plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || !user.Identity?.IsAuthenticated == true)
            return Results.Unauthorized();

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? user.Identity?.Name;

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var email = user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("email")
            ?? $"{userId}@placeholder.local";

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(userId, email, request.ProductHandle);

        if (!result.IsSuccess)
            return Results.BadRequest(new { error = result.ErrorMessage });

        response.SubscriptionId = result.SubscriptionId;
        response.PlanName = result.PlanName;
        response.PlanHandle = result.PlanHandle;
        response.Price = result.Price;
        response.State = result.State;
        response.NextBillingDate = result.NextBillingDate;
        return Results.Created("api/my-subscriptions", response);
    }
}
