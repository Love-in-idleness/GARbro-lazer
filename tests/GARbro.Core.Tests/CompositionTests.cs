using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Linq;
using Xunit;

public sealed class CompositionTests
{
    [Fact]
    public void MefPreservesExportsAndPriorityMetadata()
    {
        using var catalog = new TypeCatalog(typeof(TestExport));
        using var container = new CompositionContainer(catalog);
        var exported = container.GetExports<ITestContract, ITestMetadata>().Single();
        Assert.Equal(42, exported.Metadata.Priority);
        Assert.Equal("portable", exported.Value.Name);
    }

    public interface ITestContract
    {
        string Name { get; }
    }

    public interface ITestMetadata
    {
        int Priority { get; }
    }

    [Export(typeof(ITestContract)), ExportMetadata("Priority", 42)]
    public sealed class TestExport : ITestContract
    {
        public string Name => "portable";
    }
}
