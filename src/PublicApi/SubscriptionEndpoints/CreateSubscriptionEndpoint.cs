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
/// Idempotently subscribes the authenticated shopper to a plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService subscriptionBillingService) =>
            {
                return await HandleAsync(request, httpContext, subscriptionBillingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService subscriptionBillingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userName = ApiUserContext.GetUserName(httpContext.User);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "A planHandle is required." });
        }

        var result = await subscriptionBillingService.SubscribeAsync(userName, userName, request.PlanHandle, httpContext.RequestAborted);

        response.Subscription = SubscriptionDto.FromSubscription(result.Subscription);
        response.WasCreated = result.WasCreated;

        if (result.WasCreated)
        {
            return Results.Created($"api/subscriptions/{result.Subscription.SubscriptionId}", response);
        }

        return Results.Ok(response);
    }
}
