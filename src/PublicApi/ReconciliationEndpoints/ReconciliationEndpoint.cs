using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: lines up PayPal's own transaction records for a date range against eShop's
/// orders, so a payment PayPal knows about that eShop doesn't (or the reverse) is visible.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ReconciliationEndpoint : EndpointBaseAsync
    .WithRequest<ReconciliationRequest>
    .WithActionResult<ReconciliationResponse>
{
    private readonly IReconciliationService _reconciliationService;

    public ReconciliationEndpoint(IReconciliationService reconciliationService)
    {
        _reconciliationService = reconciliationService;
    }

    [HttpGet("api/reconciliation")]
    [SwaggerOperation(
        Summary = "Reconciles PayPal transactions against eShop orders",
        Description = "Operator action: lists PayPal's transactions for a date range against eShop's own payment records",
        OperationId = "reconciliation.report",
        Tags = new[] { "ReconciliationEndpoints" })]
    public override async Task<ActionResult<ReconciliationResponse>> HandleAsync(ReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        var response = new ReconciliationResponse(request.CorrelationId());

        var report = await _reconciliationService.BuildReportAsync(request.From, request.To, cancellationToken);

        response.MatchedInBoth = report.MatchedInBoth.Select(ReconciliationEntryDto.From).ToList();
        response.PayPalOnly = report.PayPalOnly.Select(ReconciliationEntryDto.From).ToList();
        response.EShopOnly = report.EShopOnly.Select(ReconciliationEntryDto.From).ToList();

        return response;
    }
}
