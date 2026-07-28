using VisualNotes.Core.Services;

namespace VisualNotes.UnitTests;

public sealed class LocalSearchIndexTests
{
    [Theory]
    [InlineData("Árbol, NIÑEZ", "arbol", "ninez")]
    [InlineData("HTTP servidor_respuesta()", "http", "servidor_respuesta")]
    [InlineData("東京 Σx² + E=mc^2", "東京", "σx2", "e", "mc", "2")]
    public void Tokenizer_handles_accents_unicode_code_and_formulas(string source, params string[] expected) =>
        Assert.Equal(expected, SearchTokenizer.Tokenize(source));

    [Fact]
    public void Search_returns_fragment_and_navigation_for_every_indexed_field()
    {
        var index = new LocalSearchIndex();
        var id = Guid.NewGuid();
        index.Upsert(new(id, Guid.NewGuid(), "Álgebra lineal", "clase del martes", "matriz identidad",
            "La fórmula E=mc^2 y `servidor_respuesta`", ["física", "código"]));

        var result = Assert.Single(index.Search("formula mc", new(Fields: [SearchField.Note])));

        Assert.Equal(id, result.BlockId);
        Assert.Equal(SearchField.Note, result.Navigation.Field);
        Assert.Contains("fórmula E=mc^2", result.Fragment, StringComparison.Ordinal);
        Assert.True(result.Navigation.Start >= 0);
    }

    [Fact]
    public void Filters_by_session_field_and_all_tags()
    {
        var wantedSession = Guid.NewGuid();
        var index = new LocalSearchIndex();
        index.Upsert(new(Guid.NewGuid(), wantedSession, Context: "árbol", Tags: ["clase", "biología"]));
        index.Upsert(new(Guid.NewGuid(), Guid.NewGuid(), Title: "árbol", Tags: ["clase"]));

        var results = index.Search("arbol", new(wantedSession, [SearchField.Context], ["BIOLOGÍA", "clase"]));

        Assert.Single(results);
        Assert.Equal(wantedSession, results[0].SessionId);
    }

    [Fact]
    public void Upsert_and_remove_do_not_leave_stale_postings()
    {
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var index = new LocalSearchIndex();
        index.Upsert(new(id, sessionId, Title: "anterior"));
        index.Upsert(new(id, sessionId, Title: "actualizado"));

        Assert.Empty(index.Search("anterior"));
        Assert.Single(index.Search("actualizado"));
        Assert.True(index.Remove(id));
        Assert.Empty(index.Search("actualizado"));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void Query_terms_can_match_across_indexed_fields()
    {
        var index = new LocalSearchIndex();
        index.Upsert(new(Guid.NewGuid(), Guid.NewGuid(), Title: "Teorema", Note: "demostración"));

        Assert.Single(index.Search("teorema demostracion"));
    }
}
