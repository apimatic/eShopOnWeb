using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Returns the Maxio subscriptions owned by the JWT-authenticated shopper.</summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (HttpContext context, SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var username = context.User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();

            try
            {
                return Results.Ok(await subscriptions.GetMySubscriptionsAsync(username, cancellationToken));
            }
            catch (SubscriptionValidationException)
            {
                return Results.Unauthorized();
            }
            catch (MaxioConfigurationException)
            {
                return Results.Problem("Subscription billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (MaxioApiException)
            {
                return Results.Problem("Subscription billing is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Produces<SubscriptionDto[]>()
        .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(HttpContext context, SubscriptionService subscriptions)
    {
        var username = context.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();
        return Results.Ok(await subscriptions.GetMySubscriptionsAsync(username, CancellationToken.None));
    }
}
