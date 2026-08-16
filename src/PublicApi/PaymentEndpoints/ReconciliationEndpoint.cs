using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationRequest : BaseRequest
{
    [JsonIgnore] public DateTimeOffset From { get; set; }
    [JsonIgnore] public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalNotInEShopCount { get; set; }
    public int InEShopNotInPayPalCount { get; set; }
    public List<ReconciliationRow> Transactions { get; set; } = new();
    public List<MissingInPayPalRow> InEShopNotInPayPal { get; set; } = new();
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range and lines them up against eShop
/// orders, so a payment PayPal knows about and eShop doesn't — or the reverse — is visible. Covers the whole range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        if (request.To <= request.From)
        {
            throw new PaymentValidationException("'to' must be later than 'from'. Provide ISO-8601 date-times.");
        }

        var report = await service.BuildAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            MatchedCount = report.MatchedCount,
            InPayPalNotInEShopCount = report.InPayPalNotInEShopCount,
            InEShopNotInPayPalCount = report.InEShopNotInPayPalCount,
            Transactions = new List<ReconciliationRow>(report.Transactions),
            InEShopNotInPayPal = new List<MissingInPayPalRow>(report.InEShopNotInPayPal),
            Note = report.Note
        });
    }
}
