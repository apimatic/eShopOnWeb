using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.DTOs;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the call for the same
/// plan returns the existing subscription rather than creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionEndpoint.CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal claims, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                request.Username = claims.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        return HandleAsync(request, billingService, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var user = await _userManager.FindByNameAsync(request.Username);
        if (user is null)
        {
            return Results.Unauthorized();
        }
        var email = user.Email ?? request.Username;

        try
        {
            var subscription = await billingService.SubscribeAsync(request.Username, email, request.ProductHandle, cancellationToken);
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = subscription
            };
            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }

    public class CreateSubscriptionRequest : BaseRequest
    {
        /// <summary>The handle of the plan (Maxio product) to subscribe to, e.g. from GET /api/subscription-plans.</summary>
        public string ProductHandle { get; set; } = string.Empty;

        /// <summary>Populated from the JWT by the route handler; never taken from the request body.</summary>
        public string Username { get; set; } = string.Empty;
    }

    public class CreateSubscriptionResponse : BaseResponse
    {
        public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
        {
        }

        public CustomerSubscriptionDto? Subscription { get; set; }
    }
}
