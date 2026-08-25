using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// List available subscription plans (JWT-authenticated)
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;

    public SubscriptionPlanListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await billingService.ListPlansAsync();
            response.Plans.AddRange(plans.Select(_mapper.Map<SubscriptionPlanDto>));
            return Results.Ok(response);
        }
        catch (BillingServiceException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}
