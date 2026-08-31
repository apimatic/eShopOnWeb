using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Raises a bill with the provider for one of the caller's orders. What is billed comes from the
/// order itself, not from anything the caller restates. The bill starts out not yet put to the
/// shopper. Returns the new <c>invoiceId</c> as a top-level field.
/// </summary>
public class CreateInvoiceForOrderEndpoint : IEndpoint<IResult, RaiseInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RaiseInvoiceBody body, ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (body is null || body.DueDate == default)
                {
                    return Results.BadRequest(new ErrorResponse("A due date (dueDate) is required to raise a bill."));
                }

                var customer = ToCustomerDetails(body.CustomerName, body.CustomerEmail);
                var request = new RaiseInvoiceRequest(orderId, body.DueDate, customer, buyerId, CallerIdentity.IsOperator(user));
                return await HandleAsync(request, invoiceService);
            })
            .Produces<InvoiceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(RaiseInvoiceRequest request, IInvoiceService invoiceService)
    {
        var result = await invoiceService.RaiseForOrderAsync(
            request.BuyerId, request.IsOperator, request.OrderId, request.DueDate, request.Customer);

        return InvoiceApiResults.ToHttp(result, view =>
        {
            var response = InvoiceApiResults.ToResponse(view);
            return Results.Created($"api/invoices/{response.InvoiceId}", response);
        });
    }

    private static CustomerDetails? ToCustomerDetails(string? name, string? email) =>
        name is null && email is null ? null : new CustomerDetails(name, email);
}
