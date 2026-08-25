using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent)
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService billingService)
    {
        var username = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        SubscribeResultModel result;
        try
        {
            result = await billingService.SubscribeAsync(username, request.ProductHandle);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = ToDto(result.Subscription),
            AlreadyExisted = result.AlreadyExisted
        };

        return Results.Ok(response);
    }

    internal static SubscriptionDto ToDto(SubscriptionModel model) => new SubscriptionDto
    {
        SubscriptionId = model.SubscriptionId,
        State = model.State,
        PlanName = model.PlanName,
        PlanHandle = model.PlanHandle,
        PriceInCents = model.PriceInCents,
        NextBillingDate = model.NextBillingDate,
        ActivatedAt = model.ActivatedAt
    };
}
