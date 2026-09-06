using System.Linq;
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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions of the caller. The shopper is taken from the bearer token, never from
/// the request, so one shopper cannot read another shopper's subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new ListMySubscriptionsRequest(userName), billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Lists the subscriptions of the signed-in shopper",
                description: "Returns every subscription the caller holds in the billing system, newest first."));
    }

    public async Task<IResult> HandleAsync(
        ListMySubscriptionsRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await billingService.ListSubscriptionsAsync(request.UserName, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));

        return Results.Ok(response);
    }
}
