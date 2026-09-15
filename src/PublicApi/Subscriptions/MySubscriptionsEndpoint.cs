using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Lists subscriptions belonging to the authenticated eShopOnWeb user.</summary>
public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioBillingClient, UserManager<ApplicationUser>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/my-subscriptions", async (HttpContext context, IMaxioBillingClient maxio,
            UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
                await HandleAsync(context, maxio, userManager, cancellationToken))
            .RequireAuthorization()
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(HttpContext context, IMaxioBillingClient maxio,
        UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var user = await SubscriptionEndpointHelpers.GetCurrentUserAsync(context, userManager);
        if (user is null) return Results.Unauthorized();

        try
        {
            var customer = await maxio.FindCustomerByReferenceAsync(SubscriptionEndpointHelpers.CustomerReference(user), cancellationToken);
            if (customer is null) return Results.Ok(new MySubscriptionsResponse());

            var response = new MySubscriptionsResponse();
            response.Subscriptions.AddRange((await maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .Select(SubscriptionEndpointHelpers.ToResponse));
            return Results.Ok(response);
        }
        catch (MaxioApiException)
        {
            return Results.Problem("Subscriptions are temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    // The route handler supplies HttpContext; this member satisfies the endpoint discovery contract.
    public Task<IResult> HandleAsync(IMaxioBillingClient maxio, UserManager<ApplicationUser> userManager) =>
        Task.FromResult<IResult>(Results.Unauthorized());
}
