using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan: ensures a Maxio customer exists for
/// them (idempotent) and enrolls them (idempotent - a repeat call for a plan the shopper
/// is already subscribed to returns the existing subscription rather than creating a new one).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, string, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                var userReference = user.Identity?.Name;
                if (string.IsNullOrEmpty(userReference))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, userReference, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, string userReference, IMaxioSubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        // eShopOnWeb usernames are always the account's email address (see Register.cshtml.cs),
        // so the JWT's Name claim doubles as both the Maxio customer reference and email.
        response.Subscription = await subscriptionService.SubscribeAsync(userReference, userReference, request.PlanHandle);

        return Results.Ok(response);
    }
}
