using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a subscription for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioService _maxioService;
    private HttpContext? _httpContext;

    public CreateSubscriptionEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext) =>
            {
                _httpContext = httpContext;
                return await HandleAsync(request);
            })
            .WithName("CreateSubscription")
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        try
        {
            if (_httpContext == null)
                throw new InvalidOperationException("HttpContext not available");

            // Extract user ID from JWT claims
            var userId = _httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? _httpContext.User.FindFirstValue("sub")
                ?? throw new UnauthorizedAccessException("User ID not found in token");

            var userEmail = _httpContext.User.FindFirstValue(ClaimTypes.Email)
                ?? throw new UnauthorizedAccessException("User email not found in token");

            // For this demo, use email for first/last name parts
            var nameParts = userEmail.Split('@')[0].Split('.');
            var firstName = nameParts.Length > 0 ? nameParts[0] : "User";
            var lastName = nameParts.Length > 1 ? nameParts[1] : "Account";

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return Results.BadRequest(new { error = "ProductHandle is required" });
            }

            var subscription = await _maxioService.CreateSubscriptionAsync(
                userId: userId,
                userEmail: userEmail,
                firstName: firstName,
                lastName: lastName,
                productHandle: request.ProductHandle,
                reference: request.Reference,
                ct: CancellationToken.None);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = subscription,
                Success = true
            };

            return Results.Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
