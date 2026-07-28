using Shouldly;

using Xunit;

namespace VisualNotes.UiTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowsResourceCollection
{
    public const string Name = "Windows shared resources";
}

[Collection(WindowsResourceCollection.Name)]
[Trait("Category", "UI")]
[Trait("Category", "Windows")]
public sealed class ApplicationSmokeTests
{
    [Fact]
    public void Application_assembly_can_be_loaded()
    {
        OperatingSystem.IsWindows().ShouldBeTrue("UI tests run only in the dedicated Windows job");
        typeof(global::VisualNotes.App.App).Assembly.GetName().Name.ShouldBe("VisualNotes.App");
    }
}
