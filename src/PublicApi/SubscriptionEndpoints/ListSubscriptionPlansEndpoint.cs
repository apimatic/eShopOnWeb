using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(System.Guid correlationId) : base(correlationId) { }
    public ListSubscriptionPlansResponse() { }
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Lists the subscription plans a shopper can enroll in (the products in the configured
/// Maxio product family).
/// </summary>
/// <remarks>
/// Endpoints are registered as singletons, so scoped services (which own a DbContext / HTTP
/// client state) are resolved per-request from <see cref="HttpContext.RequestServices"/> rather
/// than captured in the constructor, which would share them unsafely across concurrent requests.
/// </remarks>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListSubscriptionPlansEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async () => await HandleAsync())
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var billing = httpContext.RequestServices.GetRequiredService<ISubscriptionBillingService>();

        var plans = await billing.ListPlansAsync(httpContext.RequestAborted);

        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans.Select(SubscriptionPlanDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
