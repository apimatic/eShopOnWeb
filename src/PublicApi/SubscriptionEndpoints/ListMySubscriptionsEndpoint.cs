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
/// List My Subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioService _maxioService;
    private readonly IMaxioCustomerService _customerService;
    private readonly IUserContextService _userContextService;

    public ListMySubscriptionsEndpoint(IMaxioService maxioService, IMaxioCustomerService customerService, IUserContextService userContextService)
    {
        _maxioService = maxioService;
        _customerService = customerService;
        _userContextService = userContextService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization(JwtBearerDefaults.AuthenticationScheme);
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListMySubscriptionsResponse();

        try
        {
            var userIdClaim = _userContextService.GetCurrentUserId();
            var emailClaim = _userContextService.GetCurrentUserEmail();

            if (string.IsNullOrEmpty(userIdClaim) || string.IsNullOrEmpty(emailClaim))
            {
                response.Message = "User identity not found in token.";
                return Results.Unauthorized();
            }

            var customerId = await _customerService.GetMaxioCustomerIdAsync(userIdClaim);
            if (customerId == null)
            {
                response.Message = "No Maxio customer found for this user.";
                return Results.NotFound(response);
            }

            var subscriptions = await _maxioService.GetCustomerSubscriptionsAsync(customerId.Value);
            response.Subscriptions.AddRange(subscriptions);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Message = $"Failed to retrieve subscriptions: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}
