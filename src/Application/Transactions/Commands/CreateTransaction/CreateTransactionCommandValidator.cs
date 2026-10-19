using Mondje.Application.Transactions.Common;
using Mondje.Domain.Constants;
using Mondje.Domain.Entities;
using FluentValidation;

namespace Mondje.Application.Transactions.Commands.CreateTransaction;

public class CreateTransactionCommandValidator : AbstractValidator<CreateTransactionCommand>
{
    public CreateTransactionCommandValidator()
    {
        RuleFor(x => x.Id)
            .Must(UuidFormat.IsValid)
            .WithMessage("L'identifiant doit être un UUID valide, par exemple 550e8400-e29b-41d4-a716-446655440000.");

        RuleFor(x => x.Type)
            .NotEmpty()
            .WithMessage("Le type est requis et doit être strictement CREDIT ou DEBIT.")
            .Must(t => t is Transaction.Credit or Transaction.Debit)
            .WithMessage("Le type doit être strictement CREDIT ou DEBIT.");

        RuleFor(x => x.Amount)
            .Must(a => a.TryGetIntegerAmount(out _))
            .WithMessage("Le montant doit être un entier strictement positif en FCFA, sans décimales (ex : 15000).");

        RuleFor(x => x.Description)
            .NotNull()
            .WithMessage("La description est requise.")
            .MaximumLength(Transaction.MaxDescriptionLength)
            .WithMessage($"La description ne peut pas dépasser {Transaction.MaxDescriptionLength} caractères.");

        RuleFor(x => x.Agency)
            .Must(a => a is null || Agencies.All.Contains(a))
            .WithMessage($"L'agence doit être l'une des suivantes : {string.Join(", ", Agencies.All)}.");
    }
}
