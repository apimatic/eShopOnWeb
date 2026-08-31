using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
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
/// Corrects the due date and/or customer details a bill carries, while it has not yet been put to the
/// shopper. The amount is not correctable here — it comes from the order. Once the bill has been issued
/// or withdrawn, correcting it is refused with a 409. Scoped to the caller's own bills unless operator.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint<IResult, UpdateInvoiceRequest, IInvoicingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapMethods("api/invoices/{invoiceId}", new[] { "PATCH" },
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, UpdateInvoiceRequest request, ClaimsPrincipal user, IInvoicingService service, CancellationToken ct) =>
            {
                var requesterId = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(requesterId))
                    return Results.Unauthorized();

                request.InvoiceId = invoiceId;
                request.RequesterId = requesterId;
                request.IsOperator = CallerIdentity.IsOperator(user);
                return await HandleAsync(request, service, ct);
            })
            .Produces<UpdateInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(UpdateInvoiceRequest request, IInvoicingService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(UpdateInvoiceRequest request, IInvoicingService service, CancellationToken ct)
    {
        return await InvoicingProblem.GuardAsync(async () =>
        {
            CustomerDetails? customer = request.Customer is null
                ? null
                : new CustomerDetails(request.Customer.Name, request.Customer.Email);

            await service.CorrectInvoiceAsync(request.InvoiceId, request.DueDate, customer,
                request.RequesterId, request.IsOperator, ct);

            var response = new UpdateInvoiceResponse(request.CorrelationId())
            {
                InvoiceId = request.InvoiceId,
            };
            return Results.Ok(response);
        });
    }
}

public class UpdateInvoiceRequest : BaseRequest
{
    /// <summary>New calendar due date (optional).</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>New customer details to carry on the bill (optional).</summary>
    public CustomerDto? Customer { get; set; }

    // Server-populated from the route and token; never bound from the request body.
    [JsonIgnore] public string InvoiceId { get; set; } = string.Empty;
    [JsonIgnore] public string RequesterId { get; set; } = string.Empty;
    [JsonIgnore] public bool IsOperator { get; set; }
}

public class CustomerDto
{
    public string? Name { get; set; }
    public string? Email { get; set; }
}

public class UpdateInvoiceResponse : BaseResponse
{
    public UpdateInvoiceResponse(Guid correlationId) : base(correlationId) { }

    public UpdateInvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
}
