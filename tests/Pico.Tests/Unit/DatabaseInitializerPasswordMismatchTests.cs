using Pico.Infrastructure;
using Xunit;

namespace Pico.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="DatabaseInitializer.IsPasswordMismatch"/>.
///
/// The initializer wraps the EF migration call in a try/catch that detects
/// Postgres password-authentication failures (SQLSTATE 28P01) and logs a
/// clear recovery message pointing the reviewer at
/// <c>docker compose down -v &amp;&amp; docker compose up --build</c>.
/// Without this hint, the failure surfaces as an opaque
/// <c>Unhandled exception: Npgsql.PostgresException (0x80004005): 28P01</c>
/// that sends reviewers down the wrong path.
///
/// The test verifies the detection works for the exception shape that
/// actually escapes EF Core after a password mismatch: the SQLSTATE travels
/// as a <c>Data["SqlState"] = "28P01"</c> entry on the inner exception,
/// possibly nested several layers deep inside an AggregateException or
/// InvalidOperationException wrapper.
/// </summary>
public class DatabaseInitializerPasswordMismatchTests
{
    [Fact]
    public void IsPasswordMismatch_ReturnsTrue_WhenInnerHasSqlState28P01()
    {
        // The shape EF Core throws when Npgsql.AuthenticateSASL fails:
        // the leaf is a wrapped exception whose Data dictionary carries
        // the SQLSTATE.
        var leaf = new Exception("password authentication failed for user \"pico\"")
        {
            Data = { ["SqlState"] = "28P01" }
        };
        var aggregate = new AggregateException(
            "One or more errors occurred.",
            leaf,
            new OperationCanceledException("aborting"));

        Assert.True(DatabaseInitializer.IsPasswordMismatch(aggregate));
    }

    [Fact]
    public void IsPasswordMismatch_ReturnsTrue_WhenWrappedInGenericException()
    {
        // The shape the migrator's StartAsync surfaces in logs — an
        // InvalidOperationException wrapping the Npgsql exception.
        var wrapped = new Exception("wrapped") { Data = { ["SqlState"] = "28P01" } };
        var outer = new InvalidOperationException("outer", wrapped);

        Assert.True(DatabaseInitializer.IsPasswordMismatch(outer));
    }

    [Fact]
    public void IsPasswordMismatch_ReturnsFalse_ForConnectionRefused()
    {
        // Connection refused (SQLSTATE 08001) is not a password mismatch.
        var leaf = new Exception("could not connect")
        {
            Data = { ["SqlState"] = "08001" }
        };

        Assert.False(DatabaseInitializer.IsPasswordMismatch(leaf));
        Assert.False(DatabaseInitializer.IsPasswordMismatch(new InvalidOperationException("oops")));
        // The string "28P01" appearing somewhere in the message — but not
        // in Data["SqlState"] — must NOT trigger. Catches a naive
        // implementation that pattern-matches on the exception text.
        Assert.False(DatabaseInitializer.IsPasswordMismatch(new Exception("SqlState 28P01 was raised")));
    }
}
