using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Derives the stable per-user key that ties an eShopOnWeb user to a Maxio customer.
/// </summary>
/// <remarks>
/// <para>
/// Maxio enforces at most one customer per <c>reference</c> value, so this key — not any local
/// bookkeeping — is what makes enrolment idempotent. It must therefore be derived deterministically
/// from something that does not change between runs.
/// </para>
/// <para>
/// The eShopOnWeb user name is that something. The ASP.NET Core Identity primary key would be the more
/// conventional choice, but it is regenerated on every start when the app runs on the in-memory
/// database, which would strand the previous run's Maxio customer and create a fresh one each time.
/// </para>
/// <para>
/// The readable part of the key is sanitized down to a conservative character set; because that is
/// lossy, a short hash of the original user name is appended so two users can never collapse onto one
/// reference.
/// </para>
/// </remarks>
public static class MaxioCustomerReference
{
    private const int MaxReadableLength = 48;
    private const int HashHexLength = 12;

    public static string For(string prefix, string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A customer reference needs a user name.", nameof(userName));
        }

        var normalized = userName.Trim().ToLowerInvariant();
        var readable = Sanitize(normalized);
        var fingerprint = ShortHash(normalized);
        var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "eshoponweb" : Sanitize(prefix.Trim().ToLowerInvariant());

        return $"{safePrefix}-{readable}-{fingerprint}";
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = false;

        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }

            if (builder.Length >= MaxReadableLength)
            {
                break;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string ShortHash(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(HashHexLength);

        for (var i = 0; i < HashHexLength / 2; i++)
        {
            builder.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
