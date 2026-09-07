using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Get authenticated user's subscriptions
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly SubscriptionsService _subscriptionsService;

    public GetMySubscriptionsEndpoint(SubscriptionsService subscriptionsService)
    {
        _subscriptionsService = subscriptionsService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext) =>
            {
                return await HandleAsyncInternal(httpContext);
            })
            .Produces<GetMySubscriptionsResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        // This method signature is required by IEndpoint<IResult> but we handle the logic in the MapGet lambda
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsyncInternal(HttpContext httpContext)
    {
        var response = new GetMySubscriptionsResponse(Guid.NewGuid());

        try
        {
            // Extract user info from JWT token
            var userNameClaim = httpContext.User.FindFirst(ClaimTypes.Name);
            if (userNameClaim == null)
            {
                return Results.Unauthorized();
            }

            var userEmail = userNameClaim.Value;
            var userId = userEmail; // Use email as userId for Maxio reference

            // Extract first and last name from email or use defaults
            var nameParts = userEmail.Split('@')[0].Split('.');
            var firstName = nameParts.Length > 0 ? nameParts[0] : "User";
            var lastName = nameParts.Length > 1 ? nameParts[1] : string.Empty;

            // Get or create Maxio customer
            var customerId = await _subscriptionsService.GetOrCreateCustomerAsync(
                userId, userEmail, firstName, lastName);

            // Get customer's subscriptions
            var subscriptions = await _subscriptionsService.GetCustomerSubscriptionsAsync(customerId);
            foreach (var subscription in subscriptions)
            {
                response.Subscriptions.Add(subscription);
            }

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
