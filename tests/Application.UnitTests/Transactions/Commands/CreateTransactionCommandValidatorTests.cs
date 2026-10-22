using System.Text.Json;
using Mondje.Application.Transactions.Commands.CreateTransaction;
using NUnit.Framework;
using Shouldly;

namespace Mondje.Application.UnitTests.Transactions.Commands;

public class CreateTransactionCommandValidatorTests
{
    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private static CreateTransactionCommand Command(
        string id = "550e8400-e29b-41d4-a716-446655440000",
        string type = "CREDIT",
        string amount = "15000",
        string? description = "Vente du jour",
        string? agency = "Douala") => new()
    {
        Id = id,
        Type = type,
        Amount = Json(amount),
        Description = description!,
        Agency = agency,
    };

    [Test]
    public void A_complete_transaction_is_valid()
    {
        new CreateTransactionCommandValidator().Validate(Command()).IsValid.ShouldBeTrue();
    }

    [Test]
    public void Agency_is_optional()
    {
        new CreateTransactionCommandValidator().Validate(Command(agency: null)).IsValid.ShouldBeTrue();
    }

    [TestCase("pas-un-uuid")]
    [TestCase("550e8400e29b41d4a716446655440000")]
    public void Invalid_uuid_is_rejected(string id)
    {
        var result = new CreateTransactionCommandValidator().Validate(Command(id: id));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("UUID valide"));
    }

    [TestCase("VIREMENT")]
    [TestCase("credit")]
    [TestCase("")]
    public void Type_must_be_exactly_credit_or_debit(string type)
    {
        var result = new CreateTransactionCommandValidator().Validate(Command(type: type));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("CREDIT ou DEBIT"));
    }

    [TestCase("0")]
    [TestCase("-100")]
    [TestCase("10.5")]
    [TestCase("\"10.5\"")]
    public void Amount_must_be_a_strictly_positive_integer(string amount)
    {
        var result = new CreateTransactionCommandValidator().Validate(Command(amount: amount));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("entier strictement positif"));
    }

    [Test]
    public void Description_is_required()
    {
        var result = new CreateTransactionCommandValidator().Validate(Command(description: null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("description est requise"));
    }

    [Test]
    public void Description_cannot_exceed_255_characters()
    {
        var result = new CreateTransactionCommandValidator()
            .Validate(Command(description: new string('a', 256)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("255"));
    }

    [Test]
    public void Description_of_exactly_255_characters_is_accepted()
    {
        new CreateTransactionCommandValidator()
            .Validate(Command(description: new string('a', 255)))
            .IsValid.ShouldBeTrue();
    }

    [Test]
    public void Agency_must_belong_to_the_cooperative()
    {
        var result = new CreateTransactionCommandValidator().Validate(Command(agency: "Kribi"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("agence"));
    }
}
