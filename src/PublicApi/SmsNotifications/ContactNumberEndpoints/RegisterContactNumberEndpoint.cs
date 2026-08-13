using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number as the shopper typed it (E.164 or national).</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Two-letter ISO country code, needed when the number is in national format.</summary>
    public string? CountryCode { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(System.Guid correlationId) : base(correlationId) { }

    /// <summary>Identifier of the registered number (top-level so the flow can be driven end to end).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public string? DisplayFormat { get; set; }
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider
/// up front — one the provider does not consider a usable destination is rejected here, not when a
/// later message fails — and what gets stored is the provider's own canonical E.164 form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                ISmsProvider smsProvider,
                IRepository<ContactNumber> repository,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                    return Results.BadRequest(new { message = "A phone number is required." });

                var validation = await smsProvider.ValidateNumberAsync(
                    request.PhoneNumber, request.CountryCode, cancellationToken);

                if (!validation.IsValid || string.IsNullOrEmpty(validation.E164PhoneNumber))
                {
                    return Results.BadRequest(new
                    {
                        message = "The number is not a usable destination and was not registered.",
                        validationErrors = validation.ValidationErrors
                    });
                }

                // Dedupe on the canonical form so a shopper is not messaged twice for one device.
                var existing = await repository.ListAsync(
                    new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
                var already = existing.Find(c => c.PhoneNumber == validation.E164PhoneNumber);

                ContactNumber contactNumber;
                if (already is not null)
                {
                    contactNumber = already;
                }
                else
                {
                    contactNumber = new ContactNumber(buyerId, validation.E164PhoneNumber,
                        validation.NationalFormat, validation.CountryCode);
                    contactNumber = await repository.AddAsync(contactNumber, cancellationToken);
                }

                var response = new RegisterContactNumberResponse(request.CorrelationId())
                {
                    ContactNumberId = contactNumber.Id,
                    PhoneNumber = contactNumber.PhoneNumber,
                    DisplayFormat = contactNumber.DisplayFormat
                };
                return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }
}
