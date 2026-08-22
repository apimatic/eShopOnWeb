using System;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationQuery : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ReconciliationQuery { From = from, To = to }, service, cancellationToken);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationQuery request, IOrderPaymentService orderPaymentService)
        => HandleAsync(request, orderPaymentService, CancellationToken.None);

    private async Task<IResult> HandleAsync(ReconciliationQuery request, IOrderPaymentService orderPaymentService, CancellationToken cancellationToken)
    {
        var report = await orderPaymentService.ReconcileAsync(request.From, request.To, cancellationToken);
        return Results.Ok(OrderDtoMapper.ToResponse(report, request.CorrelationId()));
    }
}
