using System.Text.Json;
using Mondje.Application.Transactions.Commands.UpdateTransaction;
using NUnit.Framework;
using Shouldly;

namespace Mondje.Application.UnitTests.Transactions.Commands;

public class UpdateTransactionCommandValidatorTests
{
    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private static UpdateTransactionCommand Command(
        string id = "550e8400-e29b-41d4-a716-446655440000",
        string amount = "2500",
        string? description = "Libellé corrigé") => new()
    {
        Id = id,
        Amount = Json(amount),
        Description = description!,
    };

    [Test]
    public void A_valid_update_is_accepted()
    {
        new UpdateTransactionCommandValidator().Validate(Command()).IsValid.ShouldBeTrue();
    }

    [TestCase("pas-un-uuid")]
    public void Invalid_id_is_rejected(string id)
    {
        var result = new UpdateTransactionCommandValidator().Validate(Command(id: id));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("UUID valide"));
    }

    [TestCase("0")]
    [TestCase("-5")]
    [TestCase("12.75")]
    public void Invalid_amount_is_rejected(string amount)
    {
        var result = new UpdateTransactionCommandValidator().Validate(Command(amount: amount));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("entier strictement positif"));
    }

    [Test]
    public void Description_is_required_and_bounded()
    {
        var validator = new UpdateTransactionCommandValidator();

        validator.Validate(Command(description: null)).IsValid.ShouldBeFalse();
        validator.Validate(Command(description: new string('x', 256))).IsValid.ShouldBeFalse();
        validator.Validate(Command(description: new string('x', 255))).IsValid.ShouldBeTrue();
    }
}
