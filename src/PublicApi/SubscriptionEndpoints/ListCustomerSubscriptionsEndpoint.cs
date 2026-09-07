using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>
/// Lists subscriptions for the authenticated user
/// </summary>
public class ListCustomerSubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListCustomerSubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListCustomerSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListCustomerSubscriptions");
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var response = new ListCustomerSubscriptionsResponse();

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await _subscriptionService.GetCustomerSubscriptionsAsync(userId);

            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                CustomerId = s.CustomerId,
                State = s.State,
                ProductHandle = s.ProductHandle,
                ProductName = s.ProductName,
                NextAssessmentAt = s.NextAssessmentAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Error = $"Failed to retrieve subscriptions: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class ListCustomerSubscriptionsResponse : BaseResponse
{
    public ListCustomerSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListCustomerSubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
    public string? Error { get; set; }
}
