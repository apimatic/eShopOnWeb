using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest
{
    [FromQuery(Name = "from")]
    public DateTimeOffset From { get; set; }

    [FromQuery(Name = "to")]
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public IReadOnlyList<ReconciliationLine> Lines { get; set; } = new List<ReconciliationLine>();
}

/// <summary>
/// Operator report lining PayPal's own record of transactions up against eShop orders for a date
/// range, covering the whole range (chunked + fully paged), so a payment one side knows about and
/// the other doesn't is visible.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
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
        Summary = "Reconciles PayPal transactions against eShop orders (operator)",
        Description = "Lists PayPal's transactions for a date range and lines them up against eShop orders.",
        OperationId = "reconciliation.report",
        Tags = new[] { "ReconciliationEndpoints" })]
    public override async Task<ActionResult<ReconciliationResponse>> HandleAsync(
        [FromQuery] ReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.From == default || request.To == default)
        {
            throw new PaymentException("Both 'from' and 'to' ISO-8601 date-times are required.");
        }

        var report = await _reconciliationService.ReconcileAsync(request.From, request.To, cancellationToken);

        return Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Lines = report.Lines
        });
    }
}
