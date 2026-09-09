using System.Security.Claims;
using System.Threading;
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
/// Enrolls the authenticated caller in a subscription plan. JWT-authenticated; the caller's identity
/// comes from the token. Idempotent: a repeated submit returns the existing subscription rather than
/// creating a duplicate customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly ISubscriptionBillingService _billingService;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, HttpContext http) =>
                await HandleAsync(request, user, http.RequestAborted))
            .Produces<SubscribeResponse>()
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var identity = SubscriptionMappings.ToBillingIdentity(user);
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.BadRequest(new BlazorShared.Models.ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "planHandle is required. Choose one from GET /api/subscription-plans."
            });
        }

        var result = await _billingService.SubscribeAsync(identity, request.PlanHandle, cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = SubscriptionMappings.ToDto(result.Subscription),
            AlreadyExisted = result.AlreadyExisted
        };

        // A brand-new enrollment is a created resource; an idempotent re-submit is a 200.
        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }
}
