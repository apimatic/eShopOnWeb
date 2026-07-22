using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Records metered usage against the calling customer's own live subscription (UC2, customer actor).
/// </summary>
public class RecordMyUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RecordMyUsageEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/mine/usages",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ISubscriptionService subscriptionService) =>
                await HandleAsync(request, subscriptionService))
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var userReference = _httpContextAccessor.CurrentUserReference();
        if (userReference is null)
        {
            return Results.Unauthorized();
        }

        if (request.Quantity <= decimal.Zero)
        {
            return Results.BadRequest("Quantity must be greater than zero.");
        }

        var summary = await subscriptionService.RecordUsageAsync(userReference, request.Quantity, request.Memo);

        return Results.Ok(new RecordUsageResponse(request.CorrelationId())
        {
            Usage = summary.ToDto()
        });
    }
}
