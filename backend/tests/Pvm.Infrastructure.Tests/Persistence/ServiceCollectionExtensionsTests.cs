using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Tests.Persistence;

public sealed class ServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddPvmPersistence_ThrowsWhenPvmConnectionStringIsMissingOrWhitespace(string? connectionString)
    {
        var services = new ServiceCollection();
        var configuration = new TestConfiguration(connectionString);

        void Act() => services.AddPvmPersistence(configuration);

        var exception = Assert.Throws<InvalidOperationException>(Act);
        Assert.Equal("Connection string 'Pvm' is required.", exception.Message);
    }

    private sealed class TestConfiguration(string? pvmConnectionString) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "ConnectionStrings:Pvm" ? pvmConnectionString : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren()
        {
            return [];
        }

        public IChangeToken GetReloadToken()
        {
            return NoopChangeToken.Instance;
        }

        public IConfigurationSection GetSection(string key)
        {
            return new TestConfigurationSection(key, pvmConnectionString);
        }
    }

    private sealed class TestConfigurationSection(
        string key,
        string? pvmConnectionString) : IConfigurationSection
    {
        public string? this[string childKey]
        {
            get => key == "ConnectionStrings" && childKey == "Pvm" ? pvmConnectionString : null;
            set => throw new NotSupportedException();
        }

        public string Key => key;

        public string Path => key;

        public string? Value
        {
            get => key == "Pvm" ? pvmConnectionString : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren()
        {
            return [];
        }

        public IChangeToken GetReloadToken()
        {
            return NoopChangeToken.Instance;
        }

        public IConfigurationSection GetSection(string childKey)
        {
            return new TestConfigurationSection(childKey, pvmConnectionString);
        }
    }

    private sealed class NoopChangeToken : IChangeToken
    {
        public static readonly NoopChangeToken Instance = new();

        public bool HasChanged => false;

        public bool ActiveChangeCallbacks => false;

        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
        {
            return NoopDisposable.Instance;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
