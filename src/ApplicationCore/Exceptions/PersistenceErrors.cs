using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Recognises a unique-constraint rejection from the persistence layer without ApplicationCore taking a
/// dependency on Entity Framework Core. A unique index (see the EF configuration) is what actually rejects
/// a duplicate row under concurrency; this lets the application catch that rejection and treat the racing
/// request as a no-op / reload.
/// </summary>
public static class PersistenceErrors
{
    public static bool IsUniqueViolation(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var typeName = e.GetType().FullName ?? string.Empty;

            // SQL Server unique-constraint / unique-index violations.
            if (typeName == "Microsoft.Data.SqlClient.SqlException" || typeName == "System.Data.SqlClient.SqlException")
            {
                var number = TryGetInt(e, "Number");
                if (number is 2601 or 2627)
                {
                    return true;
                }
            }

            var message = e.Message ?? string.Empty;
            if ((typeName.Contains("DbUpdateException", StringComparison.Ordinal)
                 || typeName.Contains("UniqueConstraintException", StringComparison.Ordinal))
                && (message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("unique", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static int? TryGetInt(Exception e, string propertyName)
    {
        var prop = e.GetType().GetProperty(propertyName);
        var value = prop?.GetValue(e);
        return value is int i ? i : null;
    }
}
