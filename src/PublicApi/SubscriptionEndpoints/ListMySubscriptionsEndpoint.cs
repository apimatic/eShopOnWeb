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
/// Lists the authenticated shopper's subscriptions from Maxio
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal caller, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var request = new ListMySubscriptionsRequest
                {
                    ShopperEmail = caller.Identity?.Name,
                };
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ListMySubscriptionsRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.ShopperEmail))
        {
            return Results.Problem(
                detail: "The authenticated identity carries no email claim.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        try
        {
            var subscriptions = await billingService.GetSubscriptionsForShopperAsync(
                request.ShopperEmail, cancellationToken);
            response.Subscriptions.AddRange(subscriptions);
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionProblemResults.From(ex);
        }
    }
}
