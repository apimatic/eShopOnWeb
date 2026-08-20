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
using Microsoft.eShopWeb.ApplicationCore.Billing;
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
                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.ProductHandle)] = new[] { "ProductHandle is required." }
                    });
                }

                var user = await BillingEndpointUser.ResolveAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var subscription = await billingService.SubscribeAsync(
                    user,
                    request.ProductHandle.Trim(),
                    cancellationToken);
                return Results.Ok(new CreateSubscriptionResponse(SubscriptionDto.From(subscription)));
            })
            .Produces<CreateSubscriptionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}
