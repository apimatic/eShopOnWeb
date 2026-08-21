using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request,
                HttpContext context,
                UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle) || request.PlanHandle.Length > 255)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.PlanHandle)] = new[] { "PlanHandle is required and must be 255 characters or fewer." }
                    });
                }

                var user = await SubscriptionEndpointUser.FindAsync(context, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var result = await billingService.SubscribeAsync(user, request.PlanHandle, cancellationToken);
                return result.Created
                    ? Results.Created("/api/my-subscriptions", result.Subscription)
                    : Results.Ok(result.Subscription);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }
}
