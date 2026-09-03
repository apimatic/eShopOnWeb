using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the current user to a plan. Ensures a Maxio customer exists (idempotent) and enrolls them
/// (idempotent), so a double-click never creates a duplicate. POST /api/subscriptions (JWT-authenticated;
/// the subscriber is the token's identity, never the request body).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionEndpoint.SubscribeCommand, CancellationToken>
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
            (SubscribeRequestBody? body, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new SubscribeCommand(userName, body?.PlanHandle), cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var subscriber = new SubscriberIdentity(request.UserName);
            var subscription = await _billingService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

            var response = new SubscribeResponse { Subscription = subscription.ToDto() };

            // Idempotent hit → 200 OK; a newly created subscription → 201 Created.
            return subscription.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (MaxioBillingException ex)
        {
            return ex.ToResult();
        }
    }

    /// <summary>Internal command: the subscriber identity comes from the token, the plan from the body.</summary>
    public sealed record SubscribeCommand(string UserName, string? PlanHandle);
}
