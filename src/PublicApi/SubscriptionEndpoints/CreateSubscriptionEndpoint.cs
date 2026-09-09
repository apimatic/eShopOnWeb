using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan — the hero flow. Ensures a Maxio customer exists,
/// enrols them, and returns the plan/price/state/next-billing-date. Idempotent: a repeat call (e.g. a
/// double-click) returns the existing subscription instead of creating a second one.
/// POST /api/subscriptions
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, httpContext, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, IMaxioBillingService billingService)
    {
        // The caller's identity comes from the JWT (ClaimTypes.Name == the eShopOnWeb username/email).
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.Problem(
                title: "Missing plan",
                detail: "A 'planHandle' is required to subscribe.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = await billingService.SubscribeAsync(userName, request.PlanHandle, httpContext.RequestAborted);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = result.Subscription.ToDto(),
                AlreadyExisted = result.AlreadyExisted
            };

            // Idempotent hit → 200 OK; a freshly created subscription → 201 Created.
            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions", response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.Problem(
                title: "Unknown plan",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointResults.UpstreamError(ex);
        }
    }
}
