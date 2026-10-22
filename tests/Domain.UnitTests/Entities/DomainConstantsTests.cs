using Mondje.Domain.Constants;
using Mondje.Domain.Entities;
using NUnit.Framework;
using Shouldly;

namespace Mondje.Domain.UnitTests.Entities;

public class DomainConstantsTests
{
    [Test]
    public void The_cooperative_has_exactly_three_agencies()
    {
        Agencies.All.Count.ShouldBe(3);
        Agencies.All.Distinct().Count().ShouldBe(3);
    }

    [Test]
    public void Agency_names_match_the_brief()
    {
        Agencies.All.ShouldContain(Agencies.Douala);
        Agencies.All.ShouldContain(Agencies.Yaounde);
        Agencies.All.ShouldContain(Agencies.Bafoussam);
        Agencies.Yaounde.ShouldBe("Yaoundé");
    }

    [Test]
    public void Transaction_contract_matches_the_brief()
    {
        Transaction.Credit.ShouldBe("CREDIT");
        Transaction.Debit.ShouldBe("DEBIT");
        Transaction.MaxDescriptionLength.ShouldBe(255);

        Transaction.Credit.ShouldNotBe(Transaction.Debit);
    }

    [Test]
    public void Import_statuses_are_distinct_and_stable()
    {
        var statuses = new[]
        {
            ImportBatch.Pending,
            ImportBatch.Running,
            ImportBatch.Completed,
            ImportBatch.Failed,
            ImportBatch.Cancelled,
        };

        statuses.Distinct().Count().ShouldBe(statuses.Length);
        statuses.ShouldContain("COMPLETED");
    }

    [Test]
    public void Import_formats_are_txt_and_ndjson()
    {
        ImportBatch.FormatTxt.ShouldBe("TXT");
        ImportBatch.FormatNdjson.ShouldBe("NDJSON");
        ImportBatch.FormatTxt.ShouldNotBe(ImportBatch.FormatNdjson);
    }
}
