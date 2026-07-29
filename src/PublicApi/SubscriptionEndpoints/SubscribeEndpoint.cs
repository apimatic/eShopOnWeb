using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: a repeated request (e.g. a double-click)
/// re-uses the shopper's Maxio customer and does not create a duplicate subscription.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscriber = SubscriptionMapping.ResolveSubscriber(_httpContextAccessor.HttpContext?.User);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle ?? string.Empty);

            response.Subscription = SubscriptionMapping.ToDto(result.Subscription);
            response.AlreadySubscribed = result.AlreadyExisted;
            response.Message = result.AlreadyExisted
                ? $"You are already subscribed to '{result.Subscription.PlanName}'."
                : $"Subscribed to '{result.Subscription.PlanName}'. Next billing on {result.Subscription.NextBillingAt:yyyy-MM-dd}.";

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions", response);
        }
        catch (PlanNotFoundException ex)
        {
            response.Message = ex.Message;
            return Results.BadRequest(response);
        }
    }
}
