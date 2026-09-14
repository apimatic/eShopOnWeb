using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
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
/// List the caller's own subscriptions.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user,
                   ISubscriptionBillingService billingService,
                   CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                "Lists the authenticated shopper's subscriptions.",
                "Read straight from the billing system of record. Returns an empty list when the shopper has never subscribed."));
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var identity = BillingIdentityResolver.FromPrincipal(user);

        if (identity is null)
        {
            return Results.Json(
                new ErrorDetails
                {
                    StatusCode = (int)HttpStatusCode.Unauthorized,
                    Message = "The bearer token does not identify a user."
                },
                statusCode: (int)HttpStatusCode.Unauthorized);
        }

        var subscriptions = await billingService.ListSubscriptionsAsync(identity, cancellationToken);

        var response = new ListMySubscriptionsResponse
        {
            HasActiveSubscription = subscriptions.Any(subscription => subscription.IsLive)
        };

        response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));

        return Results.Ok(response);
    }
}
