using System.Security.Claims;
using System.Threading;
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
/// Subscribes the authenticated caller to a plan. Idempotent: ensures a Maxio customer exists and creates
/// the subscription; a repeated call for the same (user, plan) returns the existing subscription rather than
/// creating a second one. The subscriber identity comes from the JWT, never the request body.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
                await ExecuteAsync(request, user, billingService, cancellationToken))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    // Required by IEndpoint; the mapped route binds identity + cancellation and calls ExecuteAsync.
    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService) =>
        ExecuteAsync(request, null, billingService, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(
        SubscribeRequest request,
        ClaimsPrincipal? user,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var username = user?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        try
        {
            // In eShop the username is the user's email; it is both the customer reference and the email.
            var outcome = await billingService.SubscribeAsync(username, username, request.PlanHandle, cancellationToken);

            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = outcome.Subscription,
                AlreadySubscribed = outcome.AlreadyExisted,
                CustomerId = outcome.CustomerId
            };

            // 200 for an idempotent hit (nothing new created); 201 for a freshly created subscription.
            return outcome.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionErrorResults.From(ex);
        }
    }
}
