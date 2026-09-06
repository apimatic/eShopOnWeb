using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IMaxioSubscriptionService service) =>
            {
                return await HandleAsync(new GetMySubscriptionsRequest(user), service);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioSubscriptionService service)
    {
        var response = new GetMySubscriptionsResponse();

        try
        {
            var userId = request.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new InvalidOperationException("User ID not found in token");

            var subscriptions = await service.GetUserSubscriptionsAsync(userId);

            response.Subscriptions = subscriptions.Select(s => new SubscriptionResponse
            {
                Id = s.Id,
                Reference = s.Reference,
                State = s.State,
                ProductPriceInCents = s.ProductPriceInCents,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt
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

public class GetMySubscriptionsRequest
{
    public GetMySubscriptionsRequest(ClaimsPrincipal user) => User = user;
    public ClaimsPrincipal User { get; }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse() : base() { }
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionResponse> Subscriptions { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
