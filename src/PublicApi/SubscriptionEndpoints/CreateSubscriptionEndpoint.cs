using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan (the hero flow). Ensures a Maxio customer exists
/// for the user (idempotent), enrolls them, and returns the plan/price/state/next-billing-date.
/// The caller's identity comes from the JWT, never the request body.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Subscribes the current user to a plan"));
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, HttpContext http)
    {
        var subscriber = SubscriptionEndpointHelpers.BuildSubscriber(http.User);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var planHandle = request?.PlanHandle?.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return Results.Problem(
                detail: "A 'planHandle' is required. Call GET /api/subscription-plans to see available plans.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing plan");
        }

        var billingService = http.RequestServices.GetRequiredService<ISubscriptionBillingService>();
        try
        {
            var result = await billingService.SubscribeAsync(subscriber, planHandle, http.RequestAborted);
            var response = new CreateSubscriptionResponse
            {
                Subscription = SubscriptionEndpointHelpers.ToDto(result.Subscription),
                AlreadyExisted = result.AlreadyExisted
            };

            // Idempotent replay → 200; a newly created subscription → 201.
            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions/{response.Subscription.SubscriptionId}", response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointHelpers.ToProblem(ex);
        }
    }
}

/// <summary>Request body for <c>POST /api/subscriptions</c>.</summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (from <c>GET /api/subscription-plans</c>).</summary>
    public string? PlanHandle { get; set; }
}

/// <summary>Response for <c>POST /api/subscriptions</c>.</summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>True when an active subscription to the plan already existed (idempotent replay).</summary>
    public bool AlreadyExisted { get; set; }
}
