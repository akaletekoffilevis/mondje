using Mondje.Application.Transactions.Common;
using FluentValidation;

namespace Mondje.Application.Transactions.Commands.DeleteTransaction;

public class DeleteTransactionCommandValidator : AbstractValidator<DeleteTransactionCommand>
{
    public DeleteTransactionCommandValidator()
    {
        RuleFor(x => x.Id).Must(UuidFormat.IsValid)
            .WithMessage("L'identifiant doit être un UUID valide, par exemple 550e8400-e29b-41d4-a716-446655440000.");
    }
}
