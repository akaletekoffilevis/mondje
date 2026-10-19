using Mondje.Application.Transactions.Common;
using FluentValidation;

namespace Mondje.Application.Transactions.Queries.GetTransaction;

public class GetTransactionQueryValidator : AbstractValidator<GetTransactionQuery>
{
    public GetTransactionQueryValidator()
    {
        RuleFor(x => x.Id).Must(UuidFormat.IsValid)
            .WithMessage("L'identifiant doit être un UUID valide, par exemple 550e8400-e29b-41d4-a716-446655440000.");
    }
}
