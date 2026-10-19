namespace Mondje.Application.Common.Interfaces;

/// <summary>
/// Heavy lifting of a file import: streaming parse, staging and bulk insert. Implemented
/// in the Infrastructure layer because it relies on SqlBulkCopy and raw SQL.
/// </summary>
public interface IImportEngine
{
    Task ProcessAsync(Guid importId, CancellationToken cancellationToken);
}