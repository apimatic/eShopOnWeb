using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record CreateSubscriptionRequest(string ProductHandle);

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateSubscriptionRequest request,
                ClaimsPrincipal principal,
                ISubscriptionBillingService billing,
                CancellationToken cancellationToken) =>
            {
                var userId = SubscriptionEndpointResults.UserId(principal);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var result = await billing.SubscribeAsync(userId, request.ProductHandle, cancellationToken);
                    return result.Created
                        ? Results.Created("/api/my-subscriptions", result.Subscription)
                        : Results.Ok(result.Subscription);
                }
                catch (BillingOperationInProgressException ex)
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
                }
                catch (BillingProviderException ex)
                {
                    return SubscriptionEndpointResults.From(ex);
                }
            })
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }
}
