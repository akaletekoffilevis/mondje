using Mondje.Application.Common.Interfaces;
using Mondje.Domain.Entities;
using Mondje.Application.Transactions.Common;

namespace Mondje.Application.Imports.Commands.CreateImport;

public class CreateImportCommand : IRequest<Guid>
{
    public Guid? Id { get; set; }
    public string FileName { get; set; } = default!;
    public string StoredFileName { get; set; } = default!;
    public long FileSizeBytes { get; set; }
    public string Format { get; set; } = default!;
    public string? Agency { get; set; }
    public string? RequestId { get; set; }
}

public class CreateImportCommandHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<CreateImportCommand, Guid>
{
    public async Task<Guid> Handle(CreateImportCommand command, CancellationToken cancellationToken)
    {
        var batch = new ImportBatch
        {
            Id = command.Id ?? Guid.NewGuid(),
            FileName = command.FileName,
            StoredFileName = command.StoredFileName,
            FileSizeBytes = command.FileSizeBytes,
            Format = command.Format,
            Agency = command.Agency,
            Status = ImportBatch.Pending,
            CreatedAtUtc = Timestamp.Now(time),
            RequestId = command.RequestId,
        };

        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        return batch.Id;
    }
}