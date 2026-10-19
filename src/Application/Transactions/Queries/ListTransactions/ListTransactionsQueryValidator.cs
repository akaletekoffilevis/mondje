using Mondje.Application.Transactions.Common;
using Mondje.Domain.Entities;
using FluentValidation;

namespace Mondje.Application.Transactions.Queries.ListTransactions;

public class ListTransactionsQueryValidator : AbstractValidator<ListTransactionsQuery>
{
    public ListTransactionsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .Must(v => v is null or >= 1 and <= ListTransactionsQueryHandler.MaxLimit)
            .WithMessage($"La limite doit être comprise entre 1 et {ListTransactionsQueryHandler.MaxLimit}.");

        RuleFor(x => x.Cursor)
            .Must(c => string.IsNullOrEmpty(c) || CursorCodec.TryParse(c, out _, out _))
            .WithMessage("Le curseur de pagination est invalide.");

        RuleFor(x => x.Type)
            .Must(t => string.IsNullOrEmpty(t) || t is Transaction.Credit or Transaction.Debit)
            .WithMessage("Le filtre de type doit être CREDIT ou DEBIT.");

        RuleFor(x => x.MinAmount)
            .Must(v => v is null or >= 0)
            .WithMessage("Le montant minimum doit être positif ou nul.");

        RuleFor(x => x.MaxAmount)
            .Must(v => v is null or >= 0)
            .WithMessage("Le montant maximum doit être positif ou nul.");

        RuleFor(x => x)
            .Must(x => x.MinAmount is null || x.MaxAmount is null || x.MinAmount <= x.MaxAmount)
            .WithMessage("Le montant minimum ne peut pas dépasser le montant maximum.");

        RuleFor(x => x.Q)
            .MaximumLength(Transaction.MaxDescriptionLength)
            .WithMessage($"Le terme de recherche ne peut pas dépasser {Transaction.MaxDescriptionLength} caractères.");
    }
}
