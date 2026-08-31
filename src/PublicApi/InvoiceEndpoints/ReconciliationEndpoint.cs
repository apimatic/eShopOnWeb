using System;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Operator report: the provider's own record of bills raised in a date range, lined up against what eShop
/// believes it raised, making plain which provider bills are eShop's and which belong to other account
/// activity. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ClaimsPrincipal>
{
    private readonly IInvoiceService _invoiceService;

    public ReconciliationEndpoint(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), user, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, ClaimsPrincipal user) => HandleAsync(request, user, default);

    public async Task<IResult> HandleAsync(ReconciliationRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var report = await _invoiceService.ReconcileAsync(request.From, request.To, ct);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Summary = InvoiceMapping.ToDto(report.Summary),
            Entries = report.Entries.Select(InvoiceMapping.ToDto).ToList(),
            Note = report.Note
        };

        return Results.Ok(response);
    }
}
