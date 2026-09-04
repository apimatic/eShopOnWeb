using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, HttpContext>
{
    public async Task<IResult> HandleAsync(HttpContext context)
    {
        return await HandleAsync(context,
            context.RequestServices.GetRequiredService<ISubscriptionBillingService>());
    }

    private static async Task<IResult> HandleAsync(HttpContext context, ISubscriptionBillingService service)
    {
        try
        {
            return Results.Ok(new SubscriptionPlanListResponse
            {
                Plans = await service.GetPlansAsync(context.RequestAborted)
            });
        }
        catch (MaxioApiException)
        {
            return SubscriptionEndpointHelpers.MaxioFailure();
        }
        catch (HttpRequestException)
        {
            return SubscriptionEndpointHelpers.ServiceUnavailable();
        }
        catch (InvalidOperationException)
        {
            return SubscriptionEndpointHelpers.ServiceUnavailable();
        }
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (
                HttpContext context, ISubscriptionBillingService service) => await HandleAsync(context, service))
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}

public sealed class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionPlanListResponse() { }

    public System.Collections.Generic.IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } =
        Array.Empty<SubscriptionPlanDto>();
}
