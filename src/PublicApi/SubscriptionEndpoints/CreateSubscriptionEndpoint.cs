using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

/// <summary>Enrolls the authenticated eShop shopper in a Maxio subscription plan.</summary>
public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    // The route additionally resolves the JWT subject, which is deliberately not supplied by a client request model.
    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billing)
        => Task.FromResult<IResult>(Results.Unauthorized());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
            CreateSubscriptionRequest request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager,
            IMaxioBillingService billing,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PlanHandle))
                return Results.BadRequest(new { message = "planHandle is required." });

            var shopper = await SubscriptionEndpointHelpers.GetShopperAsync(principal, userManager);
            if (shopper is null)
                return Results.Unauthorized();

            try
            {
                var enrollment = await billing.SubscribeAsync(shopper, request.PlanHandle, cancellationToken);
                return enrollment is null
                    ? Results.BadRequest(new { message = "The requested plan is not available." })
                    : enrollment.Created
                        ? Results.Created($"api/subscriptions/{enrollment.Subscription.Id}", enrollment.Subscription)
                        : Results.Ok(enrollment.Subscription);
            }
            catch (MaxioApiException exception)
            {
                return SubscriptionEndpointHelpers.MaxioFailure(exception);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Produces<SubscriptionDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .WithTags("SubscriptionEndpoints");
    }
}
