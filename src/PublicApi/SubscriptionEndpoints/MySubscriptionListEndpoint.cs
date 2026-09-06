using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's own subscriptions.
/// </summary>
public class MySubscriptionListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly IMapper _mapper;

    public MySubscriptionListEndpoint(ISubscriptionBillingService subscriptionBillingService, IMapper mapper)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _mapper = mapper;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the caller's subscriptions",
        Description = "Reads the caller's subscriptions back from the billing system of record. Nothing is stored locally, so the list survives a restart.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var subscriber = SubscriberResolver.FromPrincipal(User);
        if (subscriber is null)
        {
            return Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await _subscriptionBillingService.GetSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));
        response.CustomerReference = response.Subscriptions.FirstOrDefault()?.CustomerReference;

        return Ok(response);
    }
}
