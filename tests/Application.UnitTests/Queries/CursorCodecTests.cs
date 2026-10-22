using ImportCursor = Mondje.Application.Imports.Queries.ListImports.CursorCodec;
using NUnit.Framework;
using Shouldly;
using TransactionCursor = Mondje.Application.Transactions.Queries.ListTransactions.CursorCodec;

namespace Mondje.Application.UnitTests.Queries;

public class CursorCodecTests
{
    [Test]
    public void Transaction_cursor_round_trips()
    {
        var time = new DateTime(2026, 10, 19, 12, 34, 56, 789, DateTimeKind.Utc);
        const string id = "550e8400-e29b-41d4-a716-446655440000";

        var cursor = TransactionCursor.Encode(time, id);

        TransactionCursor.TryParse(cursor, out var ticks, out var parsedId).ShouldBeTrue();
        ticks.ShouldBe(time.Ticks);
        parsedId.ShouldBe(id);
    }

    [Test]
    public void Transaction_cursor_is_url_safe()
    {
        var cursor = TransactionCursor.Encode(DateTime.UtcNow, Guid.NewGuid().ToString());

        cursor.ShouldNotContain("+");
        cursor.ShouldNotContain("/");
        cursor.ShouldNotContain("=");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("pas-du-base64!")]
    [TestCase("MTIzNDU2Nzg5")] // "123456789" without the ":id" separator
    [TestCase("YWJjOjEyMw==")] // "abc:123": ticks not a number
    public void Invalid_transaction_cursor_is_ignored(string? cursor)
    {
        TransactionCursor.TryParse(cursor, out _, out _).ShouldBeFalse();
    }

    [Test]
    public void Import_cursor_round_trips()
    {
        var time = new DateTime(2026, 10, 19, 1, 2, 3, 456, DateTimeKind.Utc);
        var id = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");

        var cursor = ImportCursor.Encode(time, id);

        ImportCursor.TryParse(cursor, out var ticks, out var parsedId).ShouldBeTrue();
        ticks.ShouldBe(time.Ticks);
        parsedId.ShouldBe(id);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("!!!")]
    [TestCase("YWJjOjEyMw==")]
    [TestCase("MTIzNDU2Nzg5OnotLXVuLXV1aWQ=")]
    public void Invalid_import_cursor_is_ignored(string? cursor)
    {
        ImportCursor.TryParse(cursor, out _, out _).ShouldBeFalse();
    }
}
