using System.Linq;
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
/// Lists the subscriptions currently on file for the authenticated shopper. JWT-authenticated; the caller's
/// identity comes from the token.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
                await HandleAsync(user, billing, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        if (!SubscriptionMappings.TryCreateSubscriber(user, out var subscriber))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billing.GetSubscriptionsAsync(subscriber, cancellationToken);
            var response = new MySubscriptionsResponse();
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMappings.ToDto));
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionMappings.ToProblemResult(ex);
        }
    }
}
