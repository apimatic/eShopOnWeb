using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Middleware;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// against the messaging provider and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint
{
    private readonly IPhoneNumberValidator _phoneNumberValidator;
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public CreateContactNumberEndpoint(
        IPhoneNumberValidator phoneNumberValidator,
        IRepository<ContactNumber> contactNumberRepository)
    {
        _phoneNumberValidator = phoneNumberValidator;
        _contactNumberRepository = contactNumberRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext httpContext, CancellationToken ct) =>
            {
                return await HandleAsync(request, httpContext, ct);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, HttpContext httpContext, CancellationToken ct)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        PhoneNumberValidationResult validation;
        try
        {
            validation = await _phoneNumberValidator.ValidateAsync(request.PhoneNumber, ct);
        }
        catch (MessagingException ex)
        {
            return ProviderErrorResults.Map(ex);
        }

        if (!validation.IsValid)
        {
            return Results.BadRequest(new { message = $"The number is not a usable destination: {validation.FailureReason}." });
        }

        var canonicalNumber = validation.CanonicalNumber!;
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        if (existing.Any(c => c.PhoneNumber == canonicalNumber))
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contactNumber = await _contactNumberRepository.AddAsync(new ContactNumber(buyerId, canonicalNumber), ct);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedOn = contactNumber.CreatedOn
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
