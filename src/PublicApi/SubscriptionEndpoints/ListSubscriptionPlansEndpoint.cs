using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request for <see cref="ListSubscriptionPlansEndpoint"/> (carries the request's cancellation token).</summary>
public class ListSubscriptionPlansRequest : BaseRequest
{
    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public ListSubscriptionPlansResponse() { }

    public System.Collections.Generic.List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Lists the subscription plans a shopper can subscribe to. JWT-authenticated.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest { CancellationToken = cancellationToken }, billingService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionBillingService billingService)
    {
        var plans = await billingService.GetPlansAsync(request.CancellationToken);
        var response = new ListSubscriptionPlansResponse(request.CorrelationId())
        {
            Plans = plans.Select(p => p.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}
