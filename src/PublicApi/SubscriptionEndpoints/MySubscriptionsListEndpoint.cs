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
/// Lists the authenticated shopper's subscriptions. Read-only: it never creates a billing customer
/// for a shopper who has not subscribed yet.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "List my subscriptions",
                description: "Returns the subscriptions held by the caller identified in the bearer token."));
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        if (!SubscriberFactory.TryCreate(user, out var subscriber, out var identityError))
        {
            return Results.Problem(
                detail: identityError,
                statusCode: StatusCodes.Status400BadRequest,
                title: "The caller cannot be identified");
        }

        var result = await billingService.GetSubscriptionsAsync(subscriber, cancellationToken);

        var response = new ListMySubscriptionsResponse
        {
            CustomerReference = result.CustomerReference,
            CustomerId = result.CustomerId
        };
        response.Subscriptions.AddRange(result.Subscriptions.Select(subscription => subscription.ToDto()));

        return Results.Ok(response);
    }
}
