using System.Reflection;

namespace Shop.ArchitectureTests;

public sealed record ModuleAssemblies(
    string Name,
    Assembly Domain,
    Assembly Application,
    Assembly Infrastructure,
    Assembly Contracts)
{
    public IEnumerable<Assembly> AllAssemblies => [Domain, Application, Infrastructure, Contracts];

    public override string ToString() => Name;
}

public static class Modules
{
    public static readonly IReadOnlyList<ModuleAssemblies> All =
    [
        new("Catalog",
            typeof(Catalog.Domain.AssemblyReference).Assembly,
            typeof(Catalog.Application.AssemblyReference).Assembly,
            typeof(Catalog.Infrastructure.AssemblyReference).Assembly,
            typeof(Catalog.Contracts.AssemblyReference).Assembly),
        new("Basket",
            typeof(Basket.Domain.AssemblyReference).Assembly,
            typeof(Basket.Application.AssemblyReference).Assembly,
            typeof(Basket.Infrastructure.AssemblyReference).Assembly,
            typeof(Basket.Contracts.AssemblyReference).Assembly),
        new("Ordering",
            typeof(Ordering.Domain.AssemblyReference).Assembly,
            typeof(Ordering.Application.AssemblyReference).Assembly,
            typeof(Ordering.Infrastructure.AssemblyReference).Assembly,
            typeof(Ordering.Contracts.AssemblyReference).Assembly),
        new("Payments",
            typeof(Payments.Domain.AssemblyReference).Assembly,
            typeof(Payments.Application.AssemblyReference).Assembly,
            typeof(Payments.Infrastructure.AssemblyReference).Assembly,
            typeof(Payments.Contracts.AssemblyReference).Assembly),
        new("Identity",
            typeof(Identity.Domain.AssemblyReference).Assembly,
            typeof(Identity.Application.AssemblyReference).Assembly,
            typeof(Identity.Infrastructure.AssemblyReference).Assembly,
            typeof(Identity.Contracts.AssemblyReference).Assembly),
    ];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var m in All) data.Add(m.Name);
        return data;
    }

    public static ModuleAssemblies Get(string name) => All.Single(m => m.Name == name);
}
