using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.ReconciliationRequest>
{
    private readonly IOrderPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationEndpoint(IOrderPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (DateTimeOffset? from, DateTimeOffset? to) =>
            {
                if (!from.HasValue || !to.HasValue)
                {
                    throw new PaymentValidationException("Both 'from' and 'to' ISO-8601 date-times are required.");
                }

                return await HandleAsync(new ReconciliationRequest { From = from.Value, To = to.Value });
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        var report = await _paymentService.GetReconciliationAsync(
            request.From,
            request.To,
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        return Results.Ok(report);
    }

    public class ReconciliationRequest
    {
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
    }
}
