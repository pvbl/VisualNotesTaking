using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace VisualNotes.App.Services;

using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;

/// <summary>Small, dependency-free renderer for the Markdown emitted by the review composers.</summary>
public sealed class MarkdownFlowDocumentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(18),
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(23, 32, 51))
        };
        var lines = (value as string ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var code = false;
        var codeLines = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (code)
                {
                    document.Blocks.Add(new Paragraph(new Run(string.Join(Environment.NewLine, codeLines)))
                    {
                        FontFamily = new WpfFontFamily("Consolas"),
                        Background = new SolidColorBrush(WpfColor.FromRgb(241, 245, 249)),
                        Padding = new Thickness(10)
                    });
                    codeLines.Clear();
                }
                code = !code;
                continue;
            }
            if (code) { codeLines.Add(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith('#'))
            {
                var level = line.TakeWhile(character => character == '#').Count();
                document.Blocks.Add(new Paragraph(new Run(line[level..].Trim()))
                {
                    FontSize = level switch { 1 => 28, 2 => 22, _ => 17 },
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, level == 1 ? 0 : 14, 0, 6)
                });
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
                document.Blocks.Add(new Paragraph(new Run("• " + line[2..])) { Margin = new Thickness(16, 2, 0, 2) });
            else if (line.StartsWith("![", StringComparison.Ordinal))
                document.Blocks.Add(new Paragraph(new Run("🖼 " + line)) { Foreground = WpfBrushes.SlateGray });
            else
                document.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 3, 0, 3) });
        }
        return document;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
