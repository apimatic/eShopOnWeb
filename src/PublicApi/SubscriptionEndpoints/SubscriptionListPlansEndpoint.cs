using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionListPlansEndpoint : IEndpoint<IResult>
{
    private readonly MaxioSubscriptionService _service;

    public SubscriptionListPlansEndpoint(MaxioSubscriptionService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CancellationToken ct) =>
            {
                return await HandleAsync();
            })
            .Produces<ListPlansResponse>()
            .WithName("ListSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListPlansResponse(Guid.NewGuid());

        try
        {
            var plans = await _service.GetSubscriptionPlansAsync();
            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
