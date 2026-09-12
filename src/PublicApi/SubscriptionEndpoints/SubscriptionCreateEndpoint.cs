using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Security.Claims;
using MinimalApi.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Identity.Core;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _accessor;
    public SubscriptionCreateEndpoint(IHttpContextAccessor accessor) { _accessor = accessor; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscriptionCreateRequest req, IMaxioBillingService svc) => await HandleAsync(req, svc))
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, IMaxioBillingService svc)
    {
        var user = _accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();

        var reference = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("sub") ?? "unknown";
        // Try to enrich with email from UserManager not available here; use reference as reference
        try
        {
            var sub = await svc.CreateSubscriptionAsync(reference, request.PlanHandle);
            var resp = new SubscriptionCreateResponse
            {
                SubscriptionId = sub.Id,
                State = sub.State,
                PlanHandle = sub.PlanHandle,
                NextBillingDate = sub.NextBillingDate,
            };
            return Results.Created($"api/subscriptions/{sub.Id}", resp);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 502);
        }
    }
}

public class SubscriptionCreateRequest : BaseRequest
{
    public string PlanHandle { get; init; } = "eshop-pro";
}

public class SubscriptionCreateResponse : BaseResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public string NextBillingDate { get; set; } = "";
}


