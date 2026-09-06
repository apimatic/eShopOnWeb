using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioSubscriptionService _service;
    private readonly IHttpContextAccessor _contextAccessor;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService service, IHttpContextAccessor contextAccessor)
    {
        _service = service;
        _contextAccessor = contextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        try
        {
            var context = _contextAccessor.HttpContext;
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
                return Results.Unauthorized();

            var userId = userIdClaim.Value;
            var emailClaim = context.User.FindFirst(ClaimTypes.Email);
            var email = emailClaim?.Value ?? $"{userId}@eshop.local";

            if (string.IsNullOrEmpty(request.PlanHandle))
                return Results.BadRequest("PlanHandle is required");

            var customerId = await _service.GetOrCreateCustomerAsync(userId, email, "eShop", "User");
            if (customerId == null)
                return Results.BadRequest("Failed to create or retrieve customer");

            var subscription = await _service.CreateSubscriptionAsync(userId, customerId.Value, request.PlanHandle);
            if (subscription == null)
                return Results.BadRequest("Failed to create subscription");

            return Results.Created($"api/subscriptions/{subscription.MaxioSubscriptionId}", subscription);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class CreateSubscriptionRequest
{
    public string? PlanHandle { get; set; }
}
