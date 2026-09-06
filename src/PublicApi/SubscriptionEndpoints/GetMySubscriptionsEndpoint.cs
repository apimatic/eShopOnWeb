using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioService maxioService, IRepository<UserSubscriptionMapping> mappingRepository) =>
            {
                return await HandleAsync(new GetMySubscriptionsRequest(), maxioService, httpContext, mappingRepository);
            })
            .RequireAuthorization()
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioService maxioService,
        HttpContext httpContext, IRepository<UserSubscriptionMapping> mappingRepository)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        try
        {
            var spec = new UserSubscriptionMappingByUserIdSpecification(userId);
            var userMapping = await mappingRepository.FirstOrDefaultAsync(spec);
            if (userMapping == null)
            {
                response.Subscriptions = new List<SubscriptionDto>();
                return Results.Ok(response);
            }

            var maxioSubscriptions = await maxioService.ListCustomerSubscriptionsAsync(userMapping.MaxioCustomerId);

            response.Subscriptions = maxioSubscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                ProductName = s.Product?.Name ?? "",
                ProductHandle = s.Product?.Handle ?? "",
                Price = s.Product?.PriceInCents / 100m ?? 0,
                State = s.State,
                NextBillingDate = s.NextAssessmentAt
            }).ToList();

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
        }
    }
}

public class GetMySubscriptionsRequest : BaseRequest
{
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
