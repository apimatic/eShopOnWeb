using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Ensures a billing customer exists for the user
/// (idempotently) and enrolls them. Repeated calls for the same plan are a no-op. Requires a valid JWT;
/// the subscriber identity comes from the token.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
                await HandleAsync(request, user, billingService))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.Problem(
                detail: "A 'planHandle' is required. Choose one from GET /api/subscription-plans.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Problem(
                detail: "The access token does not identify a user.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var billingUser = BillingUser.FromUserName(userName);
        var result = await billingService.SubscribeAsync(billingUser, request.PlanHandle.Trim());

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            AlreadyExisted = result.AlreadyExisted,
            Message = result.AlreadyExisted
                ? $"You are already subscribed to '{request.PlanHandle}'."
                : $"Subscription to '{request.PlanHandle}' created successfully.",
        };

        return Results.Json(
            response,
            statusCode: result.AlreadyExisted ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }
}
