using System.Text;

using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

/// <summary>Creates stable Markdown for the review input and the generated semantic document.</summary>
public static class MarkdownComposition
{
    public static string ComposeReview(NoteSession session, IEnumerable<Screenshot> captures)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(captures);
        var source = captures.Where(x => x.Status != EntityStatus.Deleted && x.IncludeInDocument)
            .OrderBy(x => x.CapturedAt).ThenBy(x => x.Id).ToArray();
        var builder = new StringBuilder().Append("# ").AppendLine(session.Name).AppendLine();

        foreach (var section in session.Sections.OrderBy(x => x.Order).ThenBy(x => x.Id))
        {
            var items = source.Where(x => x.SectionId == section.Id).ToArray();
            if (items.Length == 0) continue;
            builder.Append("## ").AppendLine(section.Title).AppendLine();
            foreach (var capture in items) AppendCapture(builder, capture);
        }

        var unsectioned = source.Where(x => x.SectionId is null ||
            session.Sections.All(section => section.Id != x.SectionId)).ToArray();
        if (unsectioned.Length > 0)
        {
            builder.AppendLine("## Sin sección").AppendLine();
            foreach (var capture in unsectioned) AppendCapture(builder, capture);
        }
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string ComposeDocument(SemanticDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder().Append("# ").AppendLine(document.Title).AppendLine();
        foreach (var section in document.Sections)
        {
            builder.Append("## ").AppendLine(section.Content).AppendLine();
            foreach (var node in section.Nodes) AppendNode(builder, node, 3);
        }
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendCapture(StringBuilder builder, Screenshot capture)
    {
        var title = string.IsNullOrWhiteSpace(capture.DisplayTitle)
            ? capture.Image is null ? "Apunte" : "Captura"
            : capture.DisplayTitle.Trim();
        builder.Append("### ").AppendLine(title);
        if (!string.IsNullOrWhiteSpace(capture.Tags))
            builder.Append("**Etiquetas:** ").AppendLine(capture.Tags.Trim()).AppendLine();
        if (!string.IsNullOrWhiteSpace(capture.UserContext))
            builder.AppendLine(capture.UserContext.Trim()).AppendLine();
        if (capture.Image is not null && !string.IsNullOrWhiteSpace(capture.Image.RelativePath))
            builder.Append("![").Append(title.Replace("]", "\\]", StringComparison.Ordinal))
                .Append("](").Append(capture.Image.RelativePath.Replace('\\', '/')).AppendLine(")").AppendLine();
    }

    private static void AppendNode(StringBuilder builder, SemanticNode node, int headingLevel)
    {
        switch (node.Type)
        {
            case SemanticNodeType.Heading:
                builder.Append(new string('#', Math.Clamp(headingLevel, 1, 6))).Append(' ')
                    .AppendLine(node.Content).AppendLine();
                break;
            case SemanticNodeType.Code:
                builder.AppendLine("```").AppendLine(node.Content).AppendLine("```").AppendLine();
                break;
            case SemanticNodeType.List:
                foreach (var line in node.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    builder.Append("- ").AppendLine(line.Trim().TrimStart('-', '*', ' '));
                builder.AppendLine();
                break;
            case SemanticNodeType.Image:
                builder.Append("![").Append(node.Content.Replace("]", "\\]", StringComparison.Ordinal))
                    .AppendLine("]()").AppendLine();
                break;
            default:
                builder.AppendLine(node.Content).AppendLine();
                break;
        }
        foreach (var child in node.Nodes) AppendNode(builder, child, headingLevel + 1);
    }
}
