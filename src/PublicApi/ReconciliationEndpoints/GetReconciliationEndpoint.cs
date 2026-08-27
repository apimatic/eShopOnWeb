using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) {}
    public GetReconciliationResponse() {}

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int UnknownToEShopCount { get; set; }
    public int MissingInPayPalCount { get; set; }
    public List<ReconciliationRow> Rows { get; set; } = new();
}

/// <summary>
/// Operator action: lists PayPal's own record of transactions over a date range and lines
/// them up against eShop orders, surfacing transactions only one side knows about.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to }, orderPaymentService);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IOrderPaymentService orderPaymentService)
    {
        var report = await orderPaymentService.GetReconciliationAsync(request.From, request.To);

        var response = new GetReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.MatchedCount,
            UnknownToEShopCount = report.UnknownToEShopCount,
            MissingInPayPalCount = report.MissingInPayPalCount,
            Rows = report.Rows
        };
        return Results.Ok(response);
    }
}
