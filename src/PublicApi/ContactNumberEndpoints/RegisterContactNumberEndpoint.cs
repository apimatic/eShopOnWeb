using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here (not when a later send fails), and the stored value is the
/// provider's own canonical E.164 form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, HttpContext httpContext)
    {
        var cancellationToken = httpContext.RequestAborted;
        var buyerId = httpContext.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { message = "A phone number is required." });

        var smsProvider = httpContext.RequestServices.GetRequiredService<ISmsProvider>();
        var repository = httpContext.RequestServices.GetRequiredService<IRepository<ContactNumber>>();

        // Reject an unusable destination up front, using the provider's own verdict.
        var validation = await smsProvider.ValidateNumberAsync(request.PhoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return Results.BadRequest(new
            {
                message = "The provider does not consider this a usable destination number.",
                validationErrors = validation.ValidationErrors
            });
        }

        var canonical = validation.CanonicalNumber;

        // Idempotent: if this shopper already registered this number, return the existing one.
        var existing = await repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return Results.Ok(new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = already.Id,
                PhoneNumber = already.PhoneNumber,
                NationalFormat = validation.NationalFormat
            });
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        await repository.AddAsync(contactNumber, cancellationToken);

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            NationalFormat = validation.NationalFormat
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
