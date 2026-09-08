using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// POST /api/subscriptions — idempotently subscribes the authenticated shopper to a plan.
/// Ensures a Maxio customer exists for the user, then enrolls them on the requested product.
/// Returns HTTP 201 when a new subscription is created and HTTP 200 when the user already
/// had a live subscription to that plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request,
                ClaimsPrincipal principal,
                ISubscriptionService subscriptions,
                ILogger<CreateSubscriptionEndpoint> logger,
                CancellationToken cancellationToken) =>
            {
                var email = SubscriptionEndpointSupport.GetUserEmail(principal);
                if (email is null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request?.ProductHandle))
                {
                    return SubscriptionEndpointSupport.BadRequest("The 'productHandle' of the plan to subscribe to is required.");
                }

                try
                {
                    var enrollment = await subscriptions
                        .EnsureSubscriptionAsync(email, request.ProductHandle, cancellationToken)
                        .ConfigureAwait(false);

                    var response = new CreateSubscriptionResponse
                    {
                        Subscription = SubscriptionDto.FromMaxio(enrollment.Subscription),
                        Created = enrollment.Created,
                    };

                    return enrollment.Created
                        ? Results.Created("api/my-subscriptions", response)
                        : Results.Ok(response);
                }
                catch (ArgumentException ex)
                {
                    return SubscriptionEndpointSupport.BadRequest(ex.Message);
                }
                catch (Exception ex) when (ex is MaxioApiException or MaxioUnavailableException)
                {
                    return SubscriptionEndpointSupport.MapMaxioFailure(ex, logger);
                }
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }
}
