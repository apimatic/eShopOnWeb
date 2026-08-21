using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliation) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, reconciliation);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliation)
    {
        var lines = await reconciliation.ReconcileAsync(request.From, request.To, default);
        return Results.Ok(new ReconciliationResponse
        {
            From = request.From,
            To = request.To,
            Lines = lines.Select(l => new ReconciliationLineDto
            {
                OrderId = l.OrderId,
                PayPalTransactionId = l.PayPalTransactionId,
                Match = l.Match,
                InvoiceId = l.InvoiceId,
                Amount = l.Amount,
                Status = l.Status,
                Note = l.Note
            }).ToList()
        });
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationLineDto> Lines { get; set; } = new();
}

public class ReconciliationLineDto
{
    public string? OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string Match { get; set; } = string.Empty;
    public string? InvoiceId { get; set; }
    public string? Amount { get; set; }
    public string? Status { get; set; }
    public string? Note { get; set; }
}
