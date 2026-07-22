using System.Security.Claims;
using System.Threading;
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
/// Report pay-as-you-go usage against a subscription (UC2). Customers may only meter their own
/// subscription; administrators may meter any.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    private const string AccruesToNextInvoice = "The recorded usage will appear on the next renewal invoice.";
    private const string TotalUnavailable = "The recorded usage will appear on the next renewal invoice. The running period-to-date total is currently unavailable.";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, HttpRequest httpRequest, ClaimsPrincipal user,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var request = RecordUsageRequest.From(await SubscriptionRequestBody.ReadAsync(httpRequest, cancellationToken));
                return await HandleAsync(subscriptionId, request, user, subscriptionService, cancellationToken);
            })
            .Accepts<RecordUsageRequest>("application/json")
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(0, request, new ClaimsPrincipal(), subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(int subscriptionId, RecordUsageRequest request, ClaimsPrincipal user,
        ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var report = await subscriptionService.RecordUsageAsync(user.ToSubscriptionActor(), subscriptionId,
            request.Quantity, request.Memo, cancellationToken);

        var response = new RecordUsageResponse(request.CorrelationId())
        {
            Usage = report.Record.ToDto(),
            PeriodToDateQuantity = report.PeriodToDateQuantity,
            UnitPrice = report.UnitPrice,
            EstimatedPeriodToDateAmount = report.EstimatedPeriodToDateAmount,
            PeriodToDateAvailable = report.PeriodToDateAvailable,
            Message = report.PeriodToDateAvailable ? AccruesToNextInvoice : TotalUnavailable
        };

        return Results.Ok(response);
    }
}
