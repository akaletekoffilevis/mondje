using Mondje.Application.Common.Interfaces;

namespace Mondje.Application.Imports.Commands.ProcessImport;

public class ProcessImportCommand : IRequest
{
    public Guid ImportId { get; set; }
}

public class ProcessImportCommandHandler(IImportEngine engine)
    : IRequestHandler<ProcessImportCommand>
{
    public Task Handle(ProcessImportCommand command, CancellationToken cancellationToken) =>
        engine.ProcessAsync(command.ImportId, cancellationToken);
}