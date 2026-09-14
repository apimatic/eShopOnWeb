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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan, creating their billing customer record if needed.
/// </summary>
/// <remarks>
/// Idempotent by design: the provider-side customer is keyed off a reference derived from the caller's
/// own identity, and an existing live subscription on the same plan is returned as-is. A double-click
/// therefore answers <c>200 OK</c> with <c>alreadySubscribed: true</c> instead of enrolling twice.
/// </remarks>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeCommand, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                var email = BillingResults.GetSubscriberEmail(user);
                if (string.IsNullOrWhiteSpace(email))
                {
                    return BillingResults.MissingIdentity();
                }

                var command = new SubscribeCommand(request.CorrelationId(), email, request.PlanHandle);
                return await HandleAsync(command, billingService, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeCommand command, ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new SubscribeResponse(command.CorrelationId());

        SubscribeResult result;
        try
        {
            result = await billingService.SubscribeAsync(
                new Subscriber(command.SubscriberEmail), command.PlanHandle, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            return BillingResults.Problem(ex);
        }

        response.Subscription = result.Subscription.ToDto();
        response.AlreadySubscribed = result.AlreadySubscribed;

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }
}
