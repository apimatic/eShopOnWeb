using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Record pay-as-you-go usage against the signed-in customer's subscription
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public RecordUsageEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ClaimsPrincipal user,
                ISubscriptionService subscriptionService) =>
            {
                request.UserReference = SubscriptionUser.GetReference(user);
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var report = await subscriptionService.RecordUsageAsync(request.UserReference, request.SubscriptionId,
            request.Quantity, request.Memo);

        response.Usage = _mapper.Map<UsageDto>(report.Receipt);
        response.PeriodToDateTotal = report.PeriodToDateTotal;
        response.IsPeriodToDateTotalAvailable = report.IsPeriodToDateTotalAvailable;

        return Results.Ok(response);
    }
}
