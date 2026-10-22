using Mondje.Application.Transactions.Queries.ListTransactions;
using NUnit.Framework;
using Shouldly;

namespace Mondje.Application.UnitTests.Queries;

public class ListTransactionsQueryValidatorTests
{
    private static ListTransactionsQuery Query() => new();

    [Test]
    public void Default_query_is_valid()
    {
        new ListTransactionsQueryValidator().Validate(Query()).IsValid.ShouldBeTrue();
    }

    [TestCase(1)]
    [TestCase(50)]
    [TestCase(ListTransactionsQueryHandler.MaxLimit)]
    public void Limit_inside_the_supported_range_is_valid(int limit)
    {
        var query = Query();
        query.Limit = limit;

        new ListTransactionsQueryValidator().Validate(query).IsValid.ShouldBeTrue();
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(ListTransactionsQueryHandler.MaxLimit + 1)]
    public void Limit_outside_the_supported_range_is_rejected(int limit)
    {
        var query = Query();
        query.Limit = limit;

        var result = new ListTransactionsQueryValidator().Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("limite"));
    }

    [Test]
    public void Invalid_cursor_is_rejected()
    {
        var query = Query();
        query.Cursor = "pas-un-curseur";

        var result = new ListTransactionsQueryValidator().Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("curseur"));
    }

    [Test]
    public void A_round_tripped_cursor_is_valid()
    {
        var query = Query();
        query.Cursor = CursorCodec.Encode(DateTime.UtcNow, Guid.NewGuid().ToString());

        new ListTransactionsQueryValidator().Validate(query).IsValid.ShouldBeTrue();
    }

    [TestCase("CREDIT")]
    [TestCase("DEBIT")]
    [TestCase(null)]
    [TestCase("")]
    public void Credit_and_debit_filters_are_valid(string? type)
    {
        var query = Query();
        query.Type = type;

        new ListTransactionsQueryValidator().Validate(query).IsValid.ShouldBeTrue();
    }

    [TestCase("VIREMENT")]
    [TestCase("credit")]
    public void Other_type_filters_are_rejected(string type)
    {
        var query = Query();
        query.Type = type;

        var result = new ListTransactionsQueryValidator().Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("CREDIT ou DEBIT"));
    }

    [Test]
    public void Negative_amounts_are_rejected()
    {
        var query = Query();
        query.MinAmount = -1;

        var result = new ListTransactionsQueryValidator().Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("positif ou nul"));
    }

    [Test]
    public void Min_amount_cannot_exceed_max_amount()
    {
        var query = Query();
        query.MinAmount = 5000;
        query.MaxAmount = 1000;

        var result = new ListTransactionsQueryValidator().Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("ne peut pas dépasser"));
    }

    [Test]
    public void Search_term_is_bounded_to_the_description_length()
    {
        var query = Query();
        query.Q = new string('a', 256);

        var result = new ListTransactionsQueryValidator().Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("255"));
    }
}
