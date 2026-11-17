using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEndpoint : EndpointBaseAsync
    .WithRequest<ReconciliationRequest>
    .WithActionResult<ReconciliationReport>
{
    private readonly IPaymentService _paymentService;

    public ReconciliationEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("api/reconciliation")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Reconcile Finix transactions against eShop orders",
        Description = "Operator action. Lists Finix authorizations and transfers in the range and lines them up against eShop orders, including both directions of mismatch.",
        OperationId = "reconciliation.get",
        Tags = new[] { "ReconciliationEndpoints" })]
    public override async Task<ActionResult<ReconciliationReport>> HandleAsync(
        ReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryParse(request.From, out var from) || !TryParse(request.To, out var to))
            {
                return BadRequest(new
                {
                    error = "from and to must be ISO-8601 date-times, for example 2026-01-01T00:00:00Z."
                });
            }

            var report = await _paymentService.GetReconciliationAsync(from, to, cancellationToken);
            return Ok(report);
        }
        catch (System.Exception ex)
        {
            return PaymentApiMapper.ErrorResult(ex);
        }
    }

    private static bool TryParse(string? value, out DateTimeOffset parsed)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);
    }
}

public class ReconciliationRequest : BaseRequest
{
    [FromQuery(Name = "from")]
    public string? From { get; set; }

    [FromQuery(Name = "to")]
    public string? To { get; set; }
}
