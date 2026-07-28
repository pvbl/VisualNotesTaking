using System.Runtime.CompilerServices;

using DiffEngine;

namespace VisualNotes.Tests.Utilities
{
    internal static class VerifyModuleInitializer
    {
        [ModuleInitializer]
        internal static void Initialize() => DiffRunner.Disabled = true;
    }
}
