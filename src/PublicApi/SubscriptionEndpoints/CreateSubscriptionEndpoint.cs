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
/// Subscribes the signed-in shopper to a plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService billing, HttpContext httpContext) =>
            {
                return await HandleAsync(request, billing, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        var shopper = await ShopperIdentity.RequireAsync(httpContext);
        var result = await billing.SubscribeAsync(shopper, request.PlanHandle, response.CorrelationId(), httpContext.RequestAborted);
        response.Subscription = SubscriptionMapper.ToDto(result.Subscription);
        if (result.Created)
            return Results.Created("api/my-subscriptions", response);
        return Results.Ok(response);
    }
}
