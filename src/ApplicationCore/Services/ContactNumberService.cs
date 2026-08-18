using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ContactNumberService : IContactNumberService
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<ContactNumberService> _logger;

    public ContactNumberService(
        IRepository<ContactNumber> repository,
        ISmsProvider smsProvider,
        IAppLogger<ContactNumberService> logger)
    {
        _repository = repository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<ContactNumber?> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawPhoneNumber, nameof(rawPhoneNumber));

        // Reject an unusable destination here, at registration — not when a later message fails to go out.
        // A transport/config failure throws SmsProviderException, which the caller surfaces as an upstream error.
        var validation = await _smsProvider.ValidateAsync(rawPhoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            // Note: the caller-supplied number is intentionally NOT logged.
            _logger.LogWarning("Rejected contact-number registration for buyer {0}: not a usable destination.", buyerId);
            return null;
        }

        // Store the provider's canonical E.164 form, never the raw caller input.
        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        await _repository.AddAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Registered contact number {0} for buyer {1}.", contactNumber.Id, buyerId);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers;
    }

    public async Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped by owner: one shopper can never delete another's number.
        var spec = new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId);
        var contactNumber = await _repository.FirstOrDefaultAsync(spec, cancellationToken);
        if (contactNumber is null)
        {
            return false;
        }

        await _repository.DeleteAsync(contactNumber, cancellationToken);
        _logger.LogInformation("Removed contact number {0} for buyer {1}.", contactNumberId, buyerId);
        return true;
    }
}
