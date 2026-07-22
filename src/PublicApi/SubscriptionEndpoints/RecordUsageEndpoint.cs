using System.Security.Claims;
using System.Threading;
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
/// Records pay-as-you-go usage against a subscription's metered component. Administrators may report
/// usage for any subscription; other callers only for their own.
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
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = SubscriptionCaller.ResolveOwnerReference(user);
                request.IsAuthenticated = user.Identity?.Name is not null;

                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (!request.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        return await SubscriptionErrorResults.ExecuteAsync(async () =>
        {
            var report = await subscriptionService.RecordUsageAsync(
                request.SubscriptionId,
                request.Quantity,
                request.Memo,
                request.OwnerReference,
                cancellationToken);

            var response = new RecordUsageResponse(request.CorrelationId())
            {
                Usage = _mapper.Map<UsageReportDto>(report)
            };

            return Results.Ok(response);
        });
    }
}
