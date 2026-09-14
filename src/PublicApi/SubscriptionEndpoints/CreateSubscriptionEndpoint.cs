using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
                CreateSubscriptionRequest request,
                ISubscriptionService service,
                HttpContext context) =>
            {
                var identity = GetIdentity(context.User);
                if (identity is null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                {
                    return Results.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]>
                    {
                        [nameof(request.ProductHandle)] = new[] { "ProductHandle is required." }
                    });
                }

                var result = await service.SubscribeAsync(
                    identity.Value.UserId,
                    identity.Value.Email,
                    request.ProductHandle,
                    context.RequestAborted);
                var response = SubscriptionDto.From(result.Subscription);
                return result.WasCreated
                    ? Results.Created("/api/my-subscriptions", response)
                    : Results.Ok(response);
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("SubscriptionEndpoints")
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static (string UserId, string Email)? GetIdentity(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email)
            ? null
            : (userId, email);
    }
}
