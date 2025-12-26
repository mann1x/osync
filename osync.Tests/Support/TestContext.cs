using osync.Tests.Infrastructure;

namespace osync.Tests.Support;

public class TestContext
{
    private readonly TestConfiguration _config;
    private readonly Dictionary<string, string> _variables;

    public TestContext(TestConfiguration config)
    {
        _config = config;
        _variables = new Dictionary<string, string>
        {
            ["{model}"] = config.RegistryModel,
            ["{remote1}"] = config.RemoteDestination1,
            ["{remote2}"] = config.RemoteDestination2
        };
    }

    public OsyncResult? LastResult { get; set; }
    public string TestModel { get; set; } = string.Empty;
    public List<string> CreatedModels { get; } = new();

    public string ResolveVariable(string variable)
    {
        return _variables.TryGetValue(variable, out var value) ? value : variable;
    }

    public string ResolveVariables(string text)
    {
        var result = text;
        foreach (var kvp in _variables)
        {
            result = result.Replace(kvp.Key, kvp.Value);
        }
        return result;
    }

    public void SetVariable(string key, string value)
    {
        _variables[key] = value;
    }

    public void AddCreatedModel(string modelName)
    {
        CreatedModels.Add(modelName);
    }
}
