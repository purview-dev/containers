# Contributing a module

How to add a new service module to `Purview.Containers`: the configuration record, builder and
container a module provides, what it may and may not contribute, and the tests it needs.

## Files

| Path | Purpose |
| --- | --- |
| `src/src/MyService/MyService.csproj` | The project; packs as `Purview.Containers.MyService` |
| `src/src/MyService/MyServiceConfiguration.cs` | Immutable configuration record with the module's fields |
| `src/src/MyService/MyServiceBuilder.cs` | The fluent builder |
| `src/src/MyService/MyServiceContainer.cs` | The container, with its connection string and endpoints |
| `src/src/MyService/Sdk/README.md` | The package README |
| `src/tests/MyService.UnitTests/` | Unit tests |
| `src/tests/MyService.IntegrationTests/` | Integration tests against a real container |

## Steps

1. **Reference the abstractions**: `<ProjectReference Include="../Core/Core.csproj" />`. A module never references a backend package; the Purview SDK supplies the pack defaults and the `InternalsVisibleTo` entries for the module's test projects.
2. **Configuration record** — derive from `ContainerConfiguration`, add module fields; credentials as `Secret`:

```csharp
public sealed record MyServiceConfiguration : ContainerConfiguration
{
    public string Username { get; init; } = "default";
    public Secret Password { get; init; } = Secret.From("default");
}
```

3. **Builder** — subclass `ContainerBuilder<TBuilder, TContainer, TConfiguration>`; default image/ports in the constructor; override `BuildConfiguration()` to snapshot module fields, and `CreateContainer(...)`:

```csharp
public class MyServiceBuilder : ContainerBuilder<MyServiceBuilder, MyServiceContainer, MyServiceConfiguration>
{
    public const ushort DefaultPort = 9000;

    public MyServiceBuilder()
    {
        WithImage("myservice:latest").WithPortBinding(DefaultPort, assignRandomHostPort: true);
    }

    public MyServiceBuilder WithPassword(string password)
    {
        this.password = Secret.From(password);
        WithEnvironment("MYSERVICE_PASSWORD", password);
        return this;
    }

    protected override MyServiceConfiguration BuildConfiguration()
    {
        MyServiceConfiguration configuration = base.BuildConfiguration();
        return configuration with
        {
            Username = username,
            Password = password,
            WaitStrategies = configuration.WaitStrategies.Count > 0
                ? configuration.WaitStrategies
                : new[] { (IWaitStrategy)Wait.ForTcpPort(DefaultPort) },
        };
    }

    protected override MyServiceContainer CreateContainer(MyServiceConfiguration configuration)
        => new(configuration, Backend);
}
```

4. **Container** — expose `GetConnectionString()` / endpoints using `GetMappedPublicPort`, built from runtime state (never cached before start). Add a provider so the polymorphic `IContainer.GetConnectionString()` works, and wire it in the builder constructor:

   ```csharp
   sealed class MyServiceConnectionStringProvider : ContainerConnectionStringProvider<MyServiceContainer, MyServiceConfiguration>
   {
       protected override string GetHostConnectionString() => Container.GetConnectionString();
   }

   // in the builder constructor:
   WithImage("myservice:latest")
       .WithPortBinding(DefaultPort, assignRandomHostPort: true)
       .WithConnectionStringProvider(new MyServiceConnectionStringProvider());
   ```
5. **Wait strategy** — prefer verifying the service itself (exec a readiness command or a host client connection), not merely that a TCP port is open. See [Wait Strategies](guide/Wait-Strategies.md). Default waits are applied in `BuildConfiguration()` unless the caller supplied their own.
6. **Secrets** — passwords/usernames go into a `Secret`-typed field; configuration `ToString()` redacts sensitive values automatically.
7. **Tests** — unit tests use `BuildConfigurationForTesting()` (internal test hook on the module builder); integration tests use TUnit, the shared `WslcTest.SkipIfUnavailableAsync()` helper from `src/tests/SharedTestingFramework`, and the real client.

## Conventions

- Package ID `Purview.Containers.MyService` (namespace prefix `Purview`).
- Module is thin: no session management, no port allocation logic, no output buffering.
- Default networking is `Bridged` (from the core defaults); ports use native random allocation unless a fixed host port is explicitly requested.
- The module is **backend-neutral** (`net10.0` and `net11.0`, references `Purview.Containers.Core` only) and therefore does **not** bring a backend or inherit its consumer requirements. A consumer references the module *and* a backend package (`Purview.Containers.Wsl` for WSLC, `Purview.Containers.Docker` for Docker). See [Consumer Requirements](Consumer-Requirements.md).
- If a capability has no implemented WSLC equivalent (e.g. UDP or interactive capture stdin), throw
  `ContainerNotSupportedException` rather than silently ignoring it. User overrides, managed networks and
  Linux capabilities use the managed WSLC SDK; only entrypoint/user/labels, copy, builds and reuse use the
  session-scoped CLI fallback.