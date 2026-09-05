using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List Subscription Plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioService _maxioService;

    public ListSubscriptionPlansEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization(JwtBearerDefaults.AuthenticationScheme);
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await _maxioService.ListSubscriptionPlansAsync();
            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Message = $"Failed to retrieve subscription plans: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}
