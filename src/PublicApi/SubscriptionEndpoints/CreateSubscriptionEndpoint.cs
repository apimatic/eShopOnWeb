using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateSubscriptionRequest request,
                ClaimsPrincipal principal,
                UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                var user = await SubscriptionEndpointUser.ResolveAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.ProductHandle)] = new[] { "ProductHandle is required." }
                    });
                }

                var enrollment = await billingService.SubscribeAsync(user, request.ProductHandle, cancellationToken);
                var response = new CreateSubscriptionResponse(
                    SubscriptionDto.From(enrollment.Subscription),
                    enrollment.Created);

                return enrollment.Created
                    ? Results.Created($"/api/subscriptions/{enrollment.Subscription.Id}", response)
                    : Results.Ok(response);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }
}
