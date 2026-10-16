using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record SubscribeRequest(string ProductHandle, string FirstName, string LastName);

public sealed class SubscriptionEndpoints : IEndpoint<IResult, HttpContext, ISubscriptionBilling>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (HttpContext context, ISubscriptionBilling billing) => Execute(context, async _ => Results.Ok(await billing.ListPlansAsync(context.RequestAborted))))
            .Produces<SubscriptionPlans>().WithTags("Subscriptions");
        app.MapPost("/api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (SubscribeRequest request, HttpContext context, ISubscriptionBilling billing) => Execute(context, async shopper =>
            {
                if (!Valid(request.ProductHandle, 255) || !Valid(request.FirstName, 100) || !Valid(request.LastName, 100))
                    return Results.BadRequest(new { message = "ProductHandle, FirstName and LastName are required and must fit their length limits (255, 100, 100)." });
                var result = await billing.SubscribeAsync(shopper, request.ProductHandle, request.FirstName.Trim(), request.LastName.Trim(), context.RequestAborted);
                if (result.Status == "pending") return Results.Json(result, statusCode: StatusCodes.Status202Accepted);
                return result.Created ? Results.Created("/api/my-subscriptions", result) : Results.Ok(result);
            })).Produces<SubscriptionEnrollment>(201).Produces<SubscriptionEnrollment>(200).Produces<SubscriptionEnrollment>(202).WithTags("Subscriptions");
        app.MapGet("/api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (HttpContext context, ISubscriptionBilling billing) => HandleAsync(context, billing))
            .Produces<ShopperSubscriptions>().WithTags("Subscriptions");
    }

    private static bool Valid(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    public Task<IResult> HandleAsync(HttpContext context, ISubscriptionBilling billing) =>
        Execute(context, async shopper => Results.Ok(await billing.ListMineAsync(shopper, context.RequestAborted)));

    private static async Task<IResult> Execute(HttpContext context, Func<BillingShopper, Task<IResult>> action)
    {
        try
        {
            var users = context.RequestServices.GetService(typeof(UserManager<ApplicationUser>)) as UserManager<ApplicationUser>;
            var name = context.User.FindFirstValue(ClaimTypes.Name);
            var user = users == null || name == null ? null : await users.FindByNameAsync(name);
            if (user == null || string.IsNullOrWhiteSpace(user.NormalizedUserName) || string.IsNullOrWhiteSpace(user.Email)) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            return await action(new BillingShopper(user.Id, user.NormalizedUserName, user.Email));
        }
        catch (BillingException ex) { return Results.Problem(ex.Message, statusCode: ex.StatusCode, title: "Subscription billing"); }
        catch (Exception) { return Results.Problem("Billing is temporarily unavailable. Please try again later.", statusCode: 503, title: "Subscription billing"); }
    }
}
