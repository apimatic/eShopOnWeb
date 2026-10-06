using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a Maxio plan (idempotent per shopper and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal caller, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                // Identity always comes from the token, never from the request body.
                request.ShopperEmail = caller.Identity?.Name;
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.ShopperEmail))
        {
            return Results.Problem(
                detail: "The authenticated identity carries no email claim.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        try
        {
            var outcome = await billingService.SubscribeAsync(
                request.ShopperEmail, request.PlanHandle ?? string.Empty, cancellationToken);

            response.Subscription = outcome.Subscription;
            response.AlreadySubscribed = outcome.AlreadySubscribed;
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionProblemResults.From(ex);
        }
    }
}
