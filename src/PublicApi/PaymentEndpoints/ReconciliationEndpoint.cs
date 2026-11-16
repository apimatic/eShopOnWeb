using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (DateTimeOffset from, DateTimeOffset to, IPaymentApplicationService payments,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var report = await payments.ReconcileAsync(from, to, cancellationToken);

                        return Results.Ok(new ReconciliationResponse
                        {
                            From = report.From,
                            To = report.To,
                            Truncated = report.Truncated,
                            NextCursor = report.NextCursor,
                            CoveredTo = report.CoveredTo,
                            ProviderTransactionCount = report.ProviderTransactionCount,
                            AppOrderCount = report.AppOrderCount,
                            Rows = report.Rows
                                .Select(row => new ReconciliationRowDto
                                {
                                    MatchStatus = row.MatchStatus,
                                    OrderId = row.OrderId,
                                    TransferId = row.TransferId,
                                    TransferType = row.TransferType,
                                    TransferState = row.TransferState,
                                    AmountCents = row.AmountCents,
                                    FeeCents = row.FeeCents,
                                    TransferCreatedAt = row.TransferCreatedAt,
                                    Note = row.Note
                                })
                                .ToList()
                        });
                    }
                    catch (PaymentOperationException exception)
                    {
                        return PaymentEndpointHelpers.Fail(exception);
                    }
                })
            .Produces<ReconciliationResponse>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("PaymentEndpoints");
    }
}
