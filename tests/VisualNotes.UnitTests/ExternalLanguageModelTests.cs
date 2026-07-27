using Xunit;

namespace VisualNotes.UnitTests;

public sealed class ExternalLanguageModelTests
{
    [Fact(Skip = "Requires real provider credentials; run explicitly after configuring secrets.")]
    [Trait("Category", "External")]
    public void Real_provider_smoke_tests_are_disabled_by_default() { }
}
