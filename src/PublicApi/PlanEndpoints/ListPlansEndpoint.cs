using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

public class ListPlansResponse : BaseResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(billing, ct);
            })
            .Produces<ListPlansResponse>()
            .WithTags("PlanEndpoints");
    }

    public async Task<IResult> HandleAsync(IBillingService billing, CancellationToken ct)
    {
        var response = new ListPlansResponse();
        response.Plans.AddRange(await billing.ListPlansAsync(ct));
        return Results.Ok(response);
    }
}
