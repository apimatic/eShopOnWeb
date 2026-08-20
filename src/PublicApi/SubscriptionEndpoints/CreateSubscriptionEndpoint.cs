using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
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
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                (CreateSubscriptionRequest request,
                    ClaimsPrincipal principal,
                    ISubscriptionService subscriptionService,
                    UserManager<ApplicationUser> userManager,
                    CancellationToken cancellationToken) =>
                    HandleAsync(request, principal, subscriptionService, userManager, cancellationToken))
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal principal,
        ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ProductHandle)] = new[] { "ProductHandle is required." }
            });
        }

        var user = await AuthenticatedBillingUserResolver.ResolveAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptionService.SubscribeAsync(
            user,
            request.ProductHandle,
            cancellationToken);
        return Results.Created("/api/my-subscriptions", new SubscriptionResponse
        {
            Subscription = SubscriptionDto.From(subscription)
        });
    }
}
