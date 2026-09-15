using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>JWT-protected subscription discovery, enrollment, and account endpoints.</summary>
public sealed class SubscriptionEndpoints
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioBillingService billing, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await billing.ListPlansAsync(cancellationToken)); }
            catch (MaxioApiException error) { return ToProblem(error); }
        }).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme }).Produces<SubscriptionPlanResponse[]>();

        app.MapPost("api/subscriptions", async (SubscribeRequest request, HttpContext context,
            UserManager<ApplicationUser> userManager, IMaxioBillingService billing, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PlanHandle))
                return Results.ValidationProblem(new[] { new KeyValuePair<string, string[]>("planHandle", ["A plan handle is required."]) }.ToDictionary(pair => pair.Key, pair => pair.Value));

            var shopper = await GetShopperAsync(context, userManager);
            if (shopper is null)
                return Results.Unauthorized();

            try { return Results.Ok(await billing.SubscribeAsync(shopper, request.PlanHandle, cancellationToken)); }
            catch (MaxioApiException error) { return ToProblem(error); }
        }).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme }).Produces<SubscriptionResponse>();

        app.MapGet("api/my-subscriptions", async (HttpContext context, UserManager<ApplicationUser> userManager,
            IMaxioBillingService billing, CancellationToken cancellationToken) =>
        {
            var shopper = await GetShopperAsync(context, userManager);
            if (shopper is null)
                return Results.Unauthorized();

            try { return Results.Ok(new MySubscriptionsResponse(await billing.ListSubscriptionsAsync(shopper, cancellationToken))); }
            catch (MaxioApiException error) { return ToProblem(error); }
        }).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme }).Produces<MySubscriptionsResponse>();
    }

    private static async Task<MaxioShopper?> GetShopperAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var userName = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            return null;

        var localPart = user.Email.Split('@', 2)[0];
        var names = localPart.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var firstName = names.FirstOrDefault() ?? "eShop";
        var lastName = names.Skip(1).FirstOrDefault() ?? "Shopper";
        return new MaxioShopper(user.Id, user.Email, firstName, lastName);
    }

    private static IResult ToProblem(MaxioApiException error)
    {
        var statusCode = error.StatusCode == HttpStatusCode.UnprocessableEntity ? StatusCodes.Status422UnprocessableEntity : StatusCodes.Status502BadGateway;
        return Results.Problem(statusCode: statusCode, title: "Subscription billing request failed", detail: error.Message);
    }
}
