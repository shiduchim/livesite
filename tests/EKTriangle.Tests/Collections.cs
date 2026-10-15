using Xunit;

namespace EKTriangle.Tests
{
    /// <summary>Tests that use the static fault-injection hooks must not run in parallel.</summary>
    [CollectionDefinition("filesystem", DisableParallelization = true)]
    public class FileSystemCollection { }
}
