using Microsoft.Win32;
using System.Windows;

namespace VisualNotes.App.ViewModels;

public interface IExportInteraction
{
    string? SelectDocxPath(string? suggestedName);
    void ShowExportSucceeded(string path);
    void ShowExportFailed(string message);
}

public sealed class WindowsExportInteraction : IExportInteraction
{
    public string? SelectDocxPath(string? suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Documento de Word (*.docx)|*.docx",
            DefaultExt = ".docx",
            AddExtension = true,
            FileName = Path.ChangeExtension(string.IsNullOrWhiteSpace(suggestedName) ? "Apuntes" : suggestedName, ".docx")
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowExportSucceeded(string path) => MessageBox.Show($"Documento exportado correctamente en:\n{path}", "Exportación completada", MessageBoxButton.OK, MessageBoxImage.Information);
    public void ShowExportFailed(string message) => MessageBox.Show(message, "No se pudo exportar", MessageBoxButton.OK, MessageBoxImage.Error);
}
