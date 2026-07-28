using Microsoft.Extensions.Logging;

namespace VisualNotes.Infrastructure.Diagnostics;

public sealed record UserFacingError(string Title, string Message, string DiagnosticId, bool IsFatal);

/// <summary>One policy for UI-dispatcher, unobserved-task and fatal process boundaries.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, Action<UserFacingError> display, Action<int> terminate)
{
    public UserFacingError HandleDispatcher(Exception exception) => Handle(exception, "No se pudo completar la operación.", false);
    public UserFacingError HandleUnobservedTask(Exception exception) => Handle(exception, "Una tarea en segundo plano falló.", false);
    public UserFacingError HandleCritical(Exception exception)
    {
        var error = Handle(exception, "VisualNotes encontró un error crítico y debe cerrarse para proteger sus datos.", true);
        terminate(-1);
        return error;
    }

    private UserFacingError Handle(Exception exception, string message, bool fatal)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var diagnosticId = Guid.NewGuid().ToString("N");
        logger.LogCritical(exception, "Unhandled failure {DiagnosticId}; fatal={Fatal}", diagnosticId, fatal);
        var result = new UserFacingError("Error de VisualNotes", $"{message}\nCódigo de diagnóstico: {diagnosticId}", diagnosticId, fatal);
        display(result);
        return result;
    }
}
