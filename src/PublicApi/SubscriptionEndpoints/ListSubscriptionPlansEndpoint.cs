using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available on this site's billing catalog.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ListSubscriptionPlansEndpoint> _logger;

    public ListSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ListSubscriptionPlansEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () => await HandleAsync())
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await _subscriptionService.GetPlansAsync(CurrentCancellationToken());
            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointResult.From(ex);
        }
        catch (System.Exception ex)
        {
            return SubscriptionEndpointResult.FromUnexpected(ex, _logger, "listing the subscription plans");
        }
    }

    private CancellationToken CurrentCancellationToken()
    {
        return _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
    }
}
