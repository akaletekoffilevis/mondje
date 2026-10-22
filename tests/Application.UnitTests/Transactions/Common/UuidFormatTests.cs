using Mondje.Application.Transactions.Common;
using NUnit.Framework;
using Shouldly;

namespace Mondje.Application.UnitTests.Transactions.Common;

public class UuidFormatTests
{
    [TestCase("550e8400-e29b-41d4-a716-446655440000")]
    [TestCase("550E8400-E29B-41D4-A716-446655440000")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    [TestCase(" 550e8400-e29b-41d4-a716-446655440000 ")]
    public void Accepts_canonical_uuids_in_any_case(string value)
    {
        UuidFormat.IsValid(value).ShouldBeTrue();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("pas-un-uuid")]
    [TestCase("550e8400e29b41d4a716446655440000")]
    [TestCase("{550e8400-e29b-41d4-a716-446655440000}")]
    [TestCase("550e8400-e29b-41d4-a716-44665544000")]
    [TestCase("550e8400-e29b-41d4-a716-4466554400000")]
    [TestCase("550e8400-e29b-41d4-a716-44665544zzzz")]
    [TestCase("550e8400-e29b-41d4-a716-446655440000;CREDIT")]
    public void Rejects_anything_that_is_not_a_canonical_uuid(string? value)
    {
        UuidFormat.IsValid(value).ShouldBeFalse();
    }
}
