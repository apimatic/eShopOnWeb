using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Endpoints;

/// <summary>
/// POST /api/subscriptions — subscribes the authenticated shopper to a plan.
///
/// Idempotent: repeated calls for the same shopper + plan return the existing Maxio
/// subscription rather than creating a duplicate (see <see cref="SubscriptionEnrollmentResult.CreatedNow"/>).
/// A Maxio customer is created for the shopper on first use (keyed by the storefront user id
/// via the customer <c>reference</c>).
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest? request,
                ClaimsPrincipal user,
                UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptions,
                CancellationToken ct) =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.BadRequest(new ProblemDetails
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "A plan handle is required.",
                        Detail = "Provide the handle of a plan returned by GET /api/subscription-plans (e.g. \"eshop-pro\")."
                    });
                }

                var userName = user.Identity?.Name;
                var shopper = userName is null ? null : await userManager.FindByNameAsync(userName);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }

                var enrollment = await subscriptions.SubscribeAsync(new SubscriptionEnrollmentInput
                {
                    CustomerReference = shopper.Id,
                    Email = shopper.Email ?? shopper.UserName ?? userName!,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    PlanHandle = request.PlanHandle.Trim()
                }, ct);

                var response = new SubscribeResponse(request.CorrelationId())
                {
                    CreatedNow = enrollment.CreatedNow,
                    Subscription = enrollment.Subscription
                };

                return Results.Ok(response);
            })
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult(Results.Ok());
}
