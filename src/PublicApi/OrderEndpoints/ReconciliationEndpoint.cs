using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IPaymentService paymentService) =>
            {
                return await HandleAsync(
                    new ReconciliationRequest
                    {
                        From = ParseTimestamp(from, "from"),
                        To = ParseTimestamp(to, "to")
                    },
                    paymentService);
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        var report = await paymentService.ReconcileAsync(request.From, request.To, CancellationToken.None);
        return Results.Ok(report);
    }

    private static DateTimeOffset ParseTimestamp(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PaymentException(400, $"The '{name}' query parameter is required and must be an ISO-8601 date-time.");
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new PaymentException(400, $"The '{name}' query parameter must be an ISO-8601 date-time.");
        }

        return parsed;
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}
