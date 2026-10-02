# Contributing a module

How to add a new service module to `Purview.Containers`.

## Files

```
src/src/MyService/
  MyService.csproj                      -> PackageId Purview.Containers.MyService
  MyServiceConfiguration.cs             -> immutable record, module fields
  MyServiceBuilder.cs                   -> fluent builder
  MyServiceContainer.cs                 -> container, connection string / endpoints
src/tests/MyService.UnitTests/
src/tests/MyService.IntegrationTests/
```

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
5. **Wait strategy** — prefer verifying the service itself (exec a readiness command or a host client connection), not merely that a TCP port is open. See [Wait Strategies](Wait-Strategies.md). Default waits are applied in `BuildConfiguration()` unless the caller supplied their own.
6. **Secrets** — passwords/usernames go into a `Secret`-typed field; configuration `ToString()` redacts sensitive values automatically.
7. **Tests** — unit tests use `BuildConfigurationForTesting()` (internal test hook on the module builder); integration tests use TUnit, the shared `WslcTest.SkipIfUnavailableAsync()` helper from `src/tests/SharedTestingFramework`, and the real client.

## Conventions

- Package ID `Purview.Containers.MyService` (namespace prefix `Purview`).
- Module is thin: no session management, no port allocation logic, no output buffering.
- Default networking is `Bridged` (from the core defaults); ports use native random allocation unless a fixed host port is explicitly requested.
- The module is **backend-neutral** (`net10.0`, references `Purview.Containers.Core` only) and therefore does **not** bring a backend or inherit its consumer requirements. A consumer references the module *and* a backend package (`Purview.Containers.Wsl` for WSLC, `Purview.Containers.Docker` for Docker). See [Consumer Requirements](Consumer-Requirements.md).
- If a Testcontainers capability has no WSLC equivalent (e.g. UDP, TTY, `--user`), throw `ContainerNotSupportedException` at build/validation rather than silently ignoring it.