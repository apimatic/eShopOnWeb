using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateRefundEndpoint : IEndpoint<IResult, CreateRefundRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, CreateRefundRequest request, IOrderPaymentService service) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { error = "idempotencyKey is required." });

                try
                {
                    var result = await service.RefundOrderAsync(orderId, request.Amount, request.IdempotencyKey);
                    var response = new CreateRefundResponse
                    {
                        RefundId = result.RefundId,
                        RefundStatus = result.RefundStatus,
                        RefundedAmount = result.RefundedAmount,
                        AlreadyExisted = result.AlreadyExisted
                    };
                    return result.AlreadyExisted
                        ? Results.Ok(response)
                        : Results.Created($"api/orders/{orderId}/refunds/{result.RefundId}", response);
                }
                catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            })
            .Produces<CreateRefundResponse>(201)
            .Produces<CreateRefundResponse>(200)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateRefundRequest request, IOrderPaymentService service)
        => await Task.FromResult(Results.StatusCode(501));
}
