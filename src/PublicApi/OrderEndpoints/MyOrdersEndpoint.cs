using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders together with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    throw new PaymentException(401, "A signed-in shopper is required.");
                }

                var orders = await paymentService.GetMyOrdersAsync(buyerId, cancellationToken);
                var response = new MyOrdersResponse(System.Guid.NewGuid());

                foreach (var item in orders)
                {
                    var dto = new MyOrderDto
                    {
                        OrderId = item.Order.Id,
                        OrderDate = item.Order.OrderDate,
                        Total = item.Order.Total(),
                        Currency = item.Payment?.Currency ?? string.Empty,
                        PaymentStatus = item.Payment?.Status.ToString() ?? OrderPaymentStatus.AwaitingPayment.ToString(),
                        Items = item.Order.OrderItems.Select(oi => new MyOrderItemDto
                        {
                            CatalogItemId = oi.ItemOrdered.CatalogItemId,
                            ProductName = oi.ItemOrdered.ProductName,
                            UnitPrice = oi.UnitPrice,
                            Units = oi.Units
                        }).ToList()
                    };

                    if (item.Payment is not null)
                    {
                        dto.Payment = new OrderPaymentDto
                        {
                            Status = item.Payment.Status.ToString(),
                            AuthorizationId = item.Payment.AuthorizationId,
                            AuthorizationState = item.Payment.AuthorizationState,
                            CaptureTransferId = item.Payment.CaptureTransferId,
                            CaptureTransferState = item.Payment.CaptureTransferState,
                            AmountAuthorized = item.Payment.AmountCents / 100m,
                            CapturedAmount = item.Payment.CapturedAmountCents.HasValue
                                ? item.Payment.CapturedAmountCents.Value / 100m
                                : null,
                            FinixFee = item.Payment.FinixFeeCents.HasValue
                                ? item.Payment.FinixFeeCents.Value / 100m
                                : null,
                            NetProceeds = item.Payment.NetAmountCents.HasValue
                                ? item.Payment.NetAmountCents.Value / 100m
                                : null,
                            RefundedAmount = item.Payment.RefundedAmountCents / 100m,
                            RefundableAmount = item.Payment.RefundableAmountCents / 100m,
                            Refunds = item.Payment.Refunds.Select(r => new RefundDto
                            {
                                RefundId = r.Id,
                                Amount = r.AmountCents / 100m,
                                State = r.State.ToString(),
                                FinixRefundId = r.FinixTransferId,
                                CreatedAt = r.CreatedAt
                            }).ToList()
                        };
                    }

                    response.Orders.Add(dto);
                }

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
