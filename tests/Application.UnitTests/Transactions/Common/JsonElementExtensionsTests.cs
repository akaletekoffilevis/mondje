using System.Text.Json;
using Mondje.Application.Transactions.Common;
using NUnit.Framework;
using Shouldly;

namespace Mondje.Application.UnitTests.Transactions.Common;

public class JsonElementExtensionsTests
{
    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    [TestCase("15000", 15000L)]
    [TestCase("1", 1L)]
    [TestCase("9223372036854775807", long.MaxValue)]
    [TestCase("\"15000\"", 15000L)]
    [TestCase("\"1\"", 1L)]
    public void Accepts_positive_integers_and_digit_strings(string raw, long expected)
    {
        Json(raw).TryGetIntegerAmount(out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("-15000")]
    [TestCase("15.5")]
    [TestCase("1e3")]
    [TestCase("\"\"")]
    [TestCase("\"15.5\"")]
    [TestCase("\"-1\"")]
    [TestCase("\"1 000\"")]
    [TestCase("\"0\"")]
    [TestCase("true")]
    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("9223372036854775808")]
    public void Rejects_zero_negative_decimals_and_non_numbers(string raw)
    {
        Json(raw).TryGetIntegerAmount(out var value).ShouldBeFalse();
        value.ShouldBe(0);
    }
}
