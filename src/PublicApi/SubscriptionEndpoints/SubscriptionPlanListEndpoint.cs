using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans offered by the configured Maxio product family
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, MaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioSubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(service, ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService service, CancellationToken ct)
    {
        var result = await service.ListPlansAsync(ct);
        return Results.Ok(new ListSubscriptionPlansResponse
        {
            Plans = result.Plans,
            IsTruncated = result.IsTruncated,
        });
    }
}

public class ListSubscriptionPlansResponse
{
    public System.Collections.Generic.IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = [];

    /// <summary>True when the plan list was cut short at the page cap.</summary>
    public bool IsTruncated { get; set; }
}
