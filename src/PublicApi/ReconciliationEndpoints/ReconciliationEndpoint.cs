using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Exceptions;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: Finix transactions in a date range matched against eShop orders.
/// The Finix list endpoints are cursor-paginated, so the whole range is covered.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, OrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([AsParameters] ReconciliationRequest request, OrderPaymentService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, OrderPaymentService service)
    {
        if (request.From is null || request.To is null)
        {
            throw new PaymentOperationException(400, "MISSING_DATE_RANGE",
                "Both 'from' and 'to' ISO-8601 date-times are required.");
        }

        var response = await service.ReconcileAsync(request.From.Value, request.To.Value, CancellationToken.None);
        return Results.Ok(response);
    }
}
