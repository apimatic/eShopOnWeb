using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>Recognises the store refusing a second insert of the same primary key (the claim guard).</summary>
internal static class StoreErrors
{
    private const string SqlExceptionTypeName = "Microsoft.Data.SqlClient.SqlException";

    public static bool IsDuplicateKey(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => false,
        // SQL Server: 2627 = PK/unique constraint violation, 2601 = unique index violation.
        DbUpdateException { InnerException: DbException db } => IsSqlServerUniqueViolation(db),
        // EF Core in-memory provider: the store's key map refuses the duplicate.
        ArgumentException arg => IsInMemoryDuplicate(arg),
        DbUpdateException { InnerException: ArgumentException inner } => IsInMemoryDuplicate(inner),
        _ => false
    };

    private static bool IsInMemoryDuplicate(ArgumentException ex) =>
        ex.Message.Contains("same key", StringComparison.OrdinalIgnoreCase);

    // Matched by name on purpose: a compile-time reference to SqlException makes this method load
    // Microsoft.Data.SqlClient into processes that never use SQL Server (e.g. the in-memory configuration),
    // where MinimalApi.Endpoint's assembly scan then fails on it.
    private static bool IsSqlServerUniqueViolation(DbException db) =>
        db.GetType().FullName == SqlExceptionTypeName
        && db.GetType().GetProperty("Number")?.GetValue(db) is int number
        && number is 2627 or 2601;
}
