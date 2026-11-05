using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    [JsonIgnore]
    public CancellationToken Cancellation { get; set; }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<PlanDto> Plans { get; set; } = new();

    /// <summary>True when the plan list hit its page ceiling and may be incomplete.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Lists the subscription plans (Maxio products of the configured product family)
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest { Cancellation = ct }, service);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, SubscriptionService service)
    {
        try
        {
            var plans = await service.ListPlansAsync(request.Cancellation);
            return Results.Ok(new ListSubscriptionPlansResponse(request.CorrelationId())
            {
                Plans = new List<PlanDto>(plans.Plans),
                Truncated = plans.Truncated
            });
        }
        catch (SubscriptionException ex)
        {
            return SubscriptionEndpointSupport.Failure(ex);
        }
    }
}
