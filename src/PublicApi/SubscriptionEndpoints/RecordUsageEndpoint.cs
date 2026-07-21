using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public double Quantity { get; set; }
    public string? Memo { get; set; }

    internal int SubscriptionId { get; set; }
    internal string CustomerReference { get; set; } = string.Empty;
    internal bool IsAdmin { get; set; }
}

public class RecordUsageResponse : BaseResponse
{
    public RecordUsageResponse(Guid correlationId) : base(correlationId) { }
    public RecordUsageResponse() { }

    public UsageDto Usage { get; set; } = null!;
}

/// <summary>
/// Records a quantity of metered usage (UC2). Customers report usage on their own subscription;
/// admins may report usage on any subscription.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, RecordUsageRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.SubscriptionId = subscriptionId;
                request.CustomerReference = user.FindFirstValue(ClaimTypes.Name)!;
                request.IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var usage = await subscriptionService.RecordUsageAsync(
            request.CustomerReference,
            request.SubscriptionId,
            request.Quantity,
            request.Memo,
            request.IsAdmin);

        response.Usage = UsageDto.FromDomain(usage);

        return Results.Ok(response);
    }
}
