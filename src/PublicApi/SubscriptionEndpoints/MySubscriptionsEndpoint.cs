using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions (GET /api/my-subscriptions)
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IMaxioBillingService _billingService;
    private readonly IMapper _mapper;

    public MySubscriptionsEndpoint(IMaxioBillingService billingService, IMapper mapper)
    {
        _billingService = billingService;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(user, ct);
            })
            .Produces<ListSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user) =>
        HandleAsync(user, CancellationToken.None);

    private async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        string identity = user.Identity?.Name ?? string.Empty;
        var response = new ListSubscriptionsResponse();

        if (string.IsNullOrWhiteSpace(identity))
        {
            return Results.Ok(response);
        }

        try
        {
            var subscriptions = await _billingService.ListSubscriptionsAsync(identity, cancellationToken);
            response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));

            return Results.Ok(response);
        }
        catch (Exception ex) when (MaxioEndpointResult.TryMapError(ex) is { } error)
        {
            return error;
        }
    }
}
