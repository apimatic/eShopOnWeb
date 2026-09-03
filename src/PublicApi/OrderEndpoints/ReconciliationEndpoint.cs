using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationRowDto
{
    public string Kind { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalInvoiceId { get; set; }
    public string? MatchStatus { get; set; }
    public string? Amount { get; set; }
    public string? Status { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EshopOrderCount { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public List<ReconciliationRowDto> Rows { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ICheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, ICheckoutService checkout) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, checkout);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, ICheckoutService checkoutService)
    {
        var report = await checkoutService.ReconcileAsync(
            request.From,
            request.To,
            _httpContextAccessor.HttpContext!.RequestAborted);

        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            EshopOrderCount = report.EshopOrderCount,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EshopOnlyCount = report.EshopOnlyCount,
            Rows = report.Rows.Select(r => new ReconciliationRowDto
            {
                Kind = r.Kind,
                OrderId = r.OrderId,
                PayPalTransactionId = r.PayPalTransactionId,
                PayPalInvoiceId = r.PayPalInvoiceId,
                MatchStatus = r.MatchStatus,
                Amount = r.Amount,
                Status = r.Status
            }).ToList()
        });
    }
}
