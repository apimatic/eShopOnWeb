using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated user to a Maxio plan (idempotent)
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscriptionCreateRequest request, IMaxioService maxioService, ClaimsPrincipal user) =>
            {
                var userName = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                request.UserName = userName;
                return await HandleAsync(request, maxioService);
            })
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, IMaxioService maxioService)
    {
        var response = new SubscriptionCreateResponse(request.CorrelationId());

        try
        {
            var subscription = await maxioService.SubscribeAsync(
                request.UserName,
                request.ProductHandle,
                request.Email,
                request.FirstName,
                request.LastName);

            response.SubscriptionId = subscription.Id;
            response.State = subscription.State;
            response.ProductId = subscription.ProductId;
            response.ProductName = subscription.Product?.Name ?? string.Empty;
            response.ProductHandle = subscription.Product?.Handle ?? string.Empty;
            response.PriceInCents = subscription.ProductPriceInCents;
            response.NextAssessmentAt = subscription.NextAssessmentAt;
            response.CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt;
            response.Currency = subscription.Currency;
            response.CreatedAt = subscription.CreatedAt;

            return Results.Created($"/api/my-subscriptions", response);
        }
        catch (HttpRequestException ex)
        {
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
        }
    }
}
