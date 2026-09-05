using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create Subscription
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioService _maxioService;
    private readonly IUserContextService _userContextService;

    public CreateSubscriptionEndpoint(IMaxioService maxioService, IUserContextService userContextService)
    {
        _maxioService = maxioService;
        _userContextService = userContextService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization(JwtBearerDefaults.AuthenticationScheme);
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userIdClaim = _userContextService.GetCurrentUserId();
            var emailClaim = _userContextService.GetCurrentUserEmail();

            if (string.IsNullOrEmpty(userIdClaim) || string.IsNullOrEmpty(emailClaim))
            {
                response.Message = "User identity not found in token.";
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                response.Message = "ProductHandle is required.";
                return Results.BadRequest(response);
            }

            var (customerId, _) = await _maxioService.GetOrCreateMaxioCustomerAsync(emailClaim, userIdClaim);
            if (!customerId.HasValue)
            {
                response.Message = "Failed to create or retrieve Maxio customer.";
                return Results.BadRequest(response);
            }

            response.Subscription = await _maxioService.CreateSubscriptionAsync(customerId.Value, request.ProductHandle);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Message = $"Failed to create subscription: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}
