using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's subscriptions, newest first. The caller's identity
/// comes from the JWT; the login name is used as the Maxio customer reference.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IMaxioBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billing, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken cancellationToken)
    {
        var callerName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(callerName))
        {
            throw new BillingException("The request is not associated with an authenticated user.", statusCode: 401);
        }

        var subscriptions = await billing.GetSubscriptionsAsync(callerName, cancellationToken);
        var response = new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}
