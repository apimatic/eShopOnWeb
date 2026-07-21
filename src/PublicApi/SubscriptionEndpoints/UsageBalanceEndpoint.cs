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

public class UsageBalanceRequest : BaseRequest
{
    internal int SubscriptionId { get; set; }
    internal string CustomerReference { get; set; } = string.Empty;
    internal bool IsAdmin { get; set; }
}

public class UsageBalanceResponse : BaseResponse
{
    public UsageBalanceResponse(Guid correlationId) : base(correlationId) { }
    public UsageBalanceResponse() { }

    public UsageBalanceDto Balance { get; set; } = null!;
}

/// <summary>Reads the current period-to-date metered usage balance for a subscription (UC2).</summary>
public class UsageBalanceEndpoint : IEndpoint<IResult, UsageBalanceRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                var request = new UsageBalanceRequest
                {
                    SubscriptionId = subscriptionId,
                    CustomerReference = user.FindFirstValue(ClaimTypes.Name)!,
                    IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS),
                };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<UsageBalanceResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(UsageBalanceRequest request, ISubscriptionService subscriptionService)
    {
        var response = new UsageBalanceResponse(request.CorrelationId());

        var balance = await subscriptionService.GetUsageBalanceAsync(request.CustomerReference, request.SubscriptionId, request.IsAdmin);
        response.Balance = UsageBalanceDto.FromDomain(balance);

        return Results.Ok(response);
    }
}
