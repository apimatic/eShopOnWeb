using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListPlansResponse()
    {
    }

    public System.Collections.Generic.List<PlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult>
{
    private readonly PlanService _plans;

    public ListPlansEndpoint(PlanService plans)
    {
        _plans = plans;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListPlansResponse();
        foreach (var product in await _plans.ListOfferedAsync())
        {
            response.Plans.Add(PlanService.ToDto(product));
        }

        return Results.Ok(response);
    }
}
