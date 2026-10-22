using Mondje.Domain.Search;
using NUnit.Framework;
using Shouldly;

namespace Mondje.Domain.UnitTests.Search;

/// <summary>
/// The word index and the user query are normalised by the same function. These tests
/// pin the contract: case-insensitive, accent-insensitive, punctuation-separated and
/// deduplicated words, truncated identically on both sides.
/// </summary>
public class SearchTokensTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\r\n")]
    public void Empty_or_blank_description_has_no_word(string? description)
    {
        SearchTokens.FromDescription(description).ShouldBeEmpty();
    }

    [Test]
    public void Words_are_split_on_spaces_and_punctuation()
    {
        var tokens = SearchTokens.FromDescription("Vente au marché, achat-carburant (urgent)!");

        tokens.ShouldBe(new[] { "vente", "au", "marche", "achat", "carburant", "urgent" });
    }

    [Test]
    public void Accents_and_case_are_normalised()
    {
        SearchTokens.FromDescription("ÉLECTRICITÉ Électricité electricite")
            .ShouldBe(new[] { "electricite" });
    }

    [Test]
    public void Duplicate_words_keep_their_first_seen_order()
    {
        SearchTokens.FromDescription("ciment Ciment beton ciment BETON")
            .ShouldBe(new[] { "ciment", "beton" });
    }

    [Test]
    public void Digits_and_mixed_words_are_kept()
    {
        SearchTokens.FromDescription("Facture F2026-001 pour 15000 FCFA")
            .ShouldBe(new[] { "facture", "f2026", "001", "pour", "15000", "fcfa" });
    }

    [Test]
    public void Query_and_description_use_the_same_normalisation()
    {
        SearchTokens.FromQuery("  Électricité  ").ShouldBe(SearchTokens.FromDescription("electricite"));
        SearchTokens.FromQuery("CIMENT").ShouldBe(SearchTokens.FromDescription("ciment"));
    }

    [Test]
    public void Overlong_words_are_truncated_to_the_index_column_size()
    {
        var word = new string('a', SearchTokens.MaxTokenLength + 40);

        var token = SearchTokens.FromDescription(word).ShouldHaveSingleItem();

        token.Length.ShouldBe(SearchTokens.MaxTokenLength);
        SearchTokens.FromQuery(word).ShouldBe(new[] { token });
    }

    [Test]
    public void An_overlong_word_matches_its_truncated_stored_form()
    {
        var prefix = new string('é', SearchTokens.MaxTokenLength + 10);
        var indexed = SearchTokens.FromDescription($"{prefix} suffixe");

        SearchTokens.FromQuery(prefix).ShouldBe(indexed.Take(1));
    }

    [TestCase("Électricité", "electricite")]
    [TestCase("Yaoundé", "yaounde")]
    [TestCase("ÇA", "ca")]
    [TestCase("DÉJÀ VU", "deja vu")]
    [TestCase("FCFA", "fcfa")]
    public void Normalize_removes_diacritics_and_lowercases(string input, string expected)
    {
        SearchTokens.Normalize(input).ShouldBe(expected);
    }

    [Test]
    public void Normalize_is_idempotent()
    {
        var once = SearchTokens.Normalize("Règlement — loyer N°12");

        SearchTokens.Normalize(once).ShouldBe(once);
    }
}
