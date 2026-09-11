using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscriptionCreateRequest req, ClaimsPrincipal user, IMaxioBillingService svc) =>
            {
                req.UserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub") ?? user.Identity?.Name ?? string.Empty;
                return await HandleAsync(req, svc);
            })
            .Produces<SubscriptionCreateResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, IMaxioBillingService svc)
    {
        var userId = request.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Results.BadRequest(new { error = "User identity missing" });

        var custId = await svc.GetCustomerReferenceAsync(userId);
        if (string.IsNullOrEmpty(custId))
        {
            var email = request.Email ?? $"{userId}@example.com";
            var first = request.FirstName ?? "User";
            var last = request.LastName ?? userId;
            custId = await svc.CreateCustomerAsync(userId, email, first, last);
        }

        var existing = await svc.CreateSubscriptionAsync(userId, request.ProductHandle);
        var response = new SubscriptionCreateResponse(request.CorrelationId())
        {
            CustomerReference = userId,
            CustomerId = int.TryParse(custId, out var c) ? c : 0,
            ProductHandle = request.ProductHandle,
            SubscriptionId = int.TryParse(existing, out var s) ? s : 0,
            State = "active"
        };
        return Results.Ok(response);
    }
}

public class SubscriptionCreateRequest : BaseRequest
{
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId) { }
    public string CustomerReference { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
}
