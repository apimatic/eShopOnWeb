using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a plan. Idempotent per (user, plan): a repeated request never creates a second
/// billing customer or subscription. 201 = created, 200 = already subscribed, 202 = outcome still being confirmed.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionBillingService>
{
    public const int MaxProductHandleLength = 255;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, httpContext, billing);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billing)
    {
        var cancellationToken = httpContext.RequestAborted;
        var productHandle = request.ProductHandle?.Trim();
        if (string.IsNullOrEmpty(productHandle) || productHandle.Length > MaxProductHandleLength)
        {
            return Results.BadRequest(new { message = "productHandle is required (a handle from GET /api/subscription-plans)." });
        }

        // Identity comes from the token only, never from the request body.
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var appUser = await userManager.FindByNameAsync(userName);
        if (appUser is null)
        {
            return Results.Unauthorized();
        }

        var result = await billing.SubscribeAsync(ToProfile(userName, appUser.Email), productHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Reference = result.Reference,
            Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription)
        };

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Outcome = "created";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadyExists:
                response.Outcome = "already_exists";
                response.Message = "You are already subscribed to this plan.";
                return Results.Ok(response);
            default:
                response.Outcome = "pending";
                response.Message = "Your subscription is being confirmed. Check GET /api/my-subscriptions shortly.";
                return Results.Accepted("api/my-subscriptions", response);
        }
    }

    /// <summary>
    /// The billing customer's identity. eShopOnWeb users have no first/last name, so the e-mail local part is
    /// used as the first name and a fixed label as the last name (both must be non-blank for Maxio).
    /// </summary>
    internal static BillingCustomerProfile ToProfile(string userName, string? email)
    {
        var address = string.IsNullOrWhiteSpace(email) ? userName : email;
        var at = address.IndexOf('@');
        var firstName = at > 0 ? address[..at] : address;
        return new BillingCustomerProfile(userName, address, firstName, "eShopOnWeb customer");
    }
}
