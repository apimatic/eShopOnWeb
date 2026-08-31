using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Operator report: the provider's own record of bills raised in a date range, lined up against what eShop
/// believes it raised, so a bill the provider knows about and eShop doesn't — or the reverse — is visible.
/// The provider account carries bills that are not this application's; each entry is labelled accordingly.
/// Restricted to the administrator role.
/// </summary>
public class ReconciliationReportEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IInvoiceService _invoiceService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationReportEndpoint(IInvoiceService invoiceService, IHttpContextAccessor httpContextAccessor)
    {
        _invoiceService = invoiceService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to));
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (request.From > request.To)
            return Results.BadRequest("'from' must not be after 'to'.");

        var context = _httpContextAccessor.HttpContext!;
        var report = await _invoiceService.ReconcileAsync(request.From, request.To, context.RequestAborted);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                InvoiceId = e.InvoiceId,
                Source = e.Source.ToString(),
                BelongsToEShop = e.BelongsToEShop,
                ProviderStatus = e.ProviderStatus,
                EShopState = e.EShopState,
                Amount = e.Amount,
                Currency = e.Currency,
                CreatedDate = e.CreatedDate,
                DueDate = e.DueDate,
                CustomerName = e.CustomerName,
                MerchantCustomerId = e.MerchantCustomerId,
            }).ToList(),
        };
        return Results.Ok(response);
    }
}
