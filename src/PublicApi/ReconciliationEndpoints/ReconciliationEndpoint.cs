using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchDto> Matches { get; set; } = new();
    public System.Collections.Generic.List<PayPalOnlyDto> PaypalOnly { get; set; } = new();
    public System.Collections.Generic.List<EShopOnlyDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Status { get; set; }
    public string? ReferenceId { get; set; }
}

public class PayPalOnlyDto
{
    public string TransactionId { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}

public class EShopOnlyDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PaypalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal Total { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderPaymentService service) =>
            {
                if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromDt)
                    || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toDt))
                {
                    throw new CheckoutException(400, "`from` and `to` must be ISO-8601 date-times.");
                }

                return await HandleAsync(new ReconciliationRequest { From = fromDt, To = toDt }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, default);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matches = report.Matches.Select(m => new ReconciliationMatchDto
            {
                OrderId = m.OrderId,
                TransactionId = m.Transaction.TransactionId,
                Amount = m.Transaction.Amount,
                Status = m.Transaction.Status,
                ReferenceId = m.Transaction.ReferenceId
            }).ToList(),
            PaypalOnly = report.PayPalOnly.Select(t => new PayPalOnlyDto
            {
                TransactionId = t.TransactionId,
                Amount = t.Amount,
                Status = t.Status,
                InitiationDate = t.InitiationDate,
                ReferenceId = t.ReferenceId,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField
            }).ToList(),
            EshopOnly = report.EShopOnly.Select(o => new EShopOnlyDto
            {
                OrderId = o.OrderId,
                Status = o.Status,
                PaypalOrderId = o.PayPalOrderId,
                AuthorizationId = o.AuthorizationId,
                CaptureId = o.CaptureId,
                Total = o.Total
            }).ToList()
        });
    }
}
