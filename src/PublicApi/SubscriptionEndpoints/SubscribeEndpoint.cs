using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// POST /api/subscriptions — subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists
/// for the eShop user (idempotent) and enrolls them. Idempotent against double-submit: an existing live
/// subscription to the same plan is returned (200) instead of creating a duplicate (201). JWT-authenticated.
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeSubscriptionRequest request,
             ISubscriptionBillingService billing,
             UserManager<ApplicationUser> userManager,
             ClaimsPrincipal principal,
             CancellationToken ct) =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.Problem(
                        title: "Invalid subscription request",
                        detail: "planHandle is required.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var user = await SubscriptionEndpointHelpers.ResolveCurrentUserAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var email = user.Email ?? user.UserName ?? user.Id;
                var (firstName, lastName) = DeriveName(email);

                try
                {
                    var outcome = await billing.SubscribeAsync(new SubscribeRequest
                    {
                        UserReference = user.Id,
                        Email = email,
                        FirstName = firstName,
                        LastName = lastName,
                        PlanHandle = request.PlanHandle
                    }, ct);

                    var response = new SubscribeResponse
                    {
                        Subscription = SubscriptionEndpointHelpers.ToDto(outcome.Subscription),
                        WasCreated = outcome.WasCreated
                    };

                    return outcome.WasCreated
                        ? Results.Created("api/my-subscriptions", response)
                        : Results.Ok(response);
                }
                catch (SubscriptionBillingException ex)
                {
                    return SubscriptionEndpointHelpers.ToProblem(ex);
                }
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags(SubscriptionEndpointHelpers.Tag);
    }

    // ApplicationUser is a bare IdentityUser with no name fields; Maxio's CreateCustomer requires first/last
    // name, so derive a reasonable placeholder from the email.
    private static (string First, string Last) DeriveName(string email)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "eShop";
        }

        return (local, "(eShopOnWeb)");
    }
}
