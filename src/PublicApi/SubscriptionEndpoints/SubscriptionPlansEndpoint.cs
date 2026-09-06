using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest, IMaxioApiClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioApiClient maxioClient) =>
            {
                return await HandleAsync(new GetSubscriptionPlansRequest(), maxioClient);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, IMaxioApiClient maxioClient)
    {
        try
        {
            var response = new GetSubscriptionPlansResponse(request.CorrelationId());
            var plans = await maxioClient.GetProductFamilyProducts("eshop-subscribe");

            foreach (var plan in plans)
            {
                response.Plans.Add(new SubscriptionPlanResponse
                {
                    Id = plan.Id,
                    Handle = plan.Handle,
                    Name = plan.Name,
                    Description = plan.Description,
                    PriceInCents = plan.PriceInCents,
                    Interval = plan.Interval,
                    IntervalUnit = plan.IntervalUnit
                });
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetSubscriptionPlansRequest : BaseRequest
{
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public GetSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
        Plans = new List<SubscriptionPlanResponse>();
    }

    public List<SubscriptionPlanResponse> Plans { get; set; }
}

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
