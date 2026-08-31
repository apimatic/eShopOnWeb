using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines
/// them up against eShop orders, so a transaction only one side knows about is visible.
/// Covers the whole range, not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, reconciliationService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliationService)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from) || !DateTimeOffset.TryParse(request.To, out var to))
        {
            return Results.BadRequest(new ReconciliationResponse { Message = "from and to must be ISO-8601 date-times." });
        }
        if (to <= from)
        {
            return Results.BadRequest(new ReconciliationResponse { Message = "to must be after from." });
        }

        try
        {
            var report = await reconciliationService.GetReconciliationAsync(from, to);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                Transactions = report.Transactions,
                MissingInEShop = report.MissingInEShop,
                MissingInPayPal = report.MissingInPayPal
            });
        }
        catch (PaymentException ex)
        {
            return Results.UnprocessableEntity(new ReconciliationResponse { Message = ex.Message });
        }
    }
}

public class ReconciliationRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationTransaction> Transactions { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationTransaction> MissingInEShop { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationLocalRecord> MissingInPayPal { get; set; } = new();
    public string? Message { get; set; }
}
