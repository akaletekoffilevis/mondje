using Mondje.Application.Transactions.Common;
using Mondje.Domain.Entities;
using FluentValidation;

namespace Mondje.Application.Transactions.Commands.UpdateTransaction;

public class UpdateTransactionCommandValidator : AbstractValidator<UpdateTransactionCommand>
{
    public UpdateTransactionCommandValidator()
    {
        RuleFor(x => x.Id)
            .Must(UuidFormat.IsValid)
            .WithMessage("L'identifiant doit être un UUID valide, par exemple 550e8400-e29b-41d4-a716-446655440000.");

        RuleFor(x => x.Amount)
            .Must(a => a.TryGetIntegerAmount(out _))
            .WithMessage("Le montant doit être un entier strictement positif en FCFA, sans décimales (ex : 15000).");

        RuleFor(x => x.Description)
            .NotNull()
            .WithMessage("La description est requise.")
            .MaximumLength(Transaction.MaxDescriptionLength)
            .WithMessage($"La description ne peut pas dépasser {Transaction.MaxDescriptionLength} caractères.");
    }
}
