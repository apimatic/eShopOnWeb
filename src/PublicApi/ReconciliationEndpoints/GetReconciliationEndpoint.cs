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

public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, ICheckoutService checkout) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to }, checkout);
            })
            .Produces<GetReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, ICheckoutService checkout)
    {
        var report = await checkout.ReconcileAsync(request.From, request.To);
        return Results.Ok(new GetReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matches = report.Matches.Select(ToDto).ToList(),
            PaypalOnly = report.PaypalOnly.Select(ToDto).ToList(),
            EShopOnly = report.EShopOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationEntryDto ToDto(ReconciliationMatch match) => new()
    {
        Status = match.Status,
        OrderId = match.OrderId,
        PaypalTransactionId = match.PaypalTransactionId,
        PaypalReferenceId = match.PaypalReferenceId,
        InvoiceId = match.InvoiceId,
        Amount = match.Amount,
        Currency = match.Currency
    };
}

public class GetReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationEntryDto> Matches { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationEntryDto> PaypalOnly { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string Status { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? PaypalTransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
}
