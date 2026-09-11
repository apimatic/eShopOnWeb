using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = "eshop-pro";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public long PriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public bool Created { get; set; }
}

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpCtx;
    public CreateSubscriptionEndpoint(IHttpContextAccessor httpCtx) { _httpCtx = httpCtx; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest req, IMaxioBillingService svc) =>
            {
                return await HandleAsync(req, svc);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService svc)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        var ctx = _httpCtx.HttpContext;
        var userId = ctx?.User?.Identity?.Name ?? ctx?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var email = ctx?.User?.FindFirst("email")?.Value ?? ctx?.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? $"{userId}@example.com";
        var firstName = ctx?.User?.FindFirst("given_name")?.Value ?? ctx?.User?.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? "User";
        var lastName = ctx?.User?.FindFirst("family_name")?.Value ?? ctx?.User?.FindFirst(System.Security.Claims.ClaimTypes.Surname)?.Value ?? userId;

        var customer = await svc.FindOrCreateCustomerAsync(userId, email, firstName, lastName);
        var existing = await svc.FindSubscriptionAsync(userId, request.ProductHandle);
        if (existing != null)
        {
            response.SubscriptionId = existing.Id;
            response.ProductHandle = request.ProductHandle;
            response.State = existing.State;
            response.PriceInCents = existing.PriceInCents;
            response.CurrentPeriodEndsAt = existing.CurrentPeriodEndsAt;
            response.Created = false;
            return Results.Ok(response);
        }

        var sub = await svc.CreateSubscriptionAsync(customer.Id.ToString(), request.ProductHandle);
        response.SubscriptionId = sub.Id;
        response.ProductHandle = request.ProductHandle;
        response.State = sub.State;
        response.PriceInCents = sub.PriceInCents;
        response.CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt;
        response.Created = true;
        return Results.Ok(response);
    }
}
