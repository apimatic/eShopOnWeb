using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to; optional when the deployment configures a default plan.</summary>
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>False when the user already had a subscription and it is returned as-is.</summary>
    public bool Created { get; set; }
}

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent per user)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest? request, HttpContext context) =>
                await HandleAsync(request ?? new CreateSubscriptionRequest(), context))
           .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
           .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext context)
    {
        var service = context.RequestServices.GetRequiredService<ISubscriptionService>();
        var result = await service.SubscribeAsync(context.User, request.PlanHandle, context.RequestAborted);
        var response = new CreateSubscriptionResponse { Subscription = result.Subscription, Created = result.Created };
        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
