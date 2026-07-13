namespace PodcastsHosting.EndToEndTests;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Xunit.Abstractions;

internal sealed class DockerComposeEnvironment : IAsyncDisposable
{
    private static readonly TimeSpan ComposeUpTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(3);
    private readonly IReadOnlyDictionary<string, string> _environmentVariables;
    private readonly ITestOutputHelper _output;
    private bool _disposed;

    private DockerComposeEnvironment(
        string repositoryRoot,
        string projectName,
        int appPort,
        int azuritePort,
        string azuriteAccountKey,
        string sqlPassword,
        ITestOutputHelper output)
    {
        RepositoryRoot = repositoryRoot;
        ProjectName = projectName;
        BaseAddress = new Uri($"http://127.0.0.1:{appPort}/");
        BlobConnectionString =
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={azuriteAccountKey};" +
            $"BlobEndpoint=http://127.0.0.1:{azuritePort}/devstoreaccount1;";
        _output = output;
        _environmentVariables = new Dictionary<string, string>
        {
            ["E2E_APP_PORT"] = appPort.ToString(),
            ["E2E_AZURITE_PORT"] = azuritePort.ToString(),
            ["MSSQL_SA_PASSWORD"] = sqlPassword,
            ["AZURITE_ACCOUNT_KEY"] = azuriteAccountKey,
            ["COMPOSE_PROGRESS"] = "plain"
        };
    }

    public string RepositoryRoot { get; }

    public string ProjectName { get; }

    public Uri BaseAddress { get; }

    public string BlobConnectionString { get; }

    public static async Task<DockerComposeEnvironment> StartAsync(ITestOutputHelper output)
    {
        var environment = new DockerComposeEnvironment(
            FindRepositoryRoot(),
            $"podcasts-e2e-{Guid.NewGuid():N}"[..26],
            GetAvailablePort(),
            GetAvailablePort(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            $"E2e_{Guid.NewGuid():N}aA1!",
            output);

        try
        {
            await environment.RunComposeAsync(
                ["up", "-d", "--build", "--remove-orphans"],
                ComposeUpTimeout);
            await environment.WaitUntilReadyAsync();
            return environment;
        }
        catch
        {
            await environment.WriteDiagnosticsAsync();
            await environment.DisposeAsync();
            throw;
        }
    }

    public HttpClient CreateHttpClient(bool allowAutoRedirect = false)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };

        return new HttpClient(handler)
        {
            BaseAddress = BaseAddress,
            Timeout = TimeSpan.FromMinutes(30)
        };
    }

    public async Task WriteDiagnosticsAsync()
    {
        try
        {
            var ps = await RunComposeAsync(["ps", "--all"], TimeSpan.FromMinutes(1), throwOnFailure: false);
            var applicationLogs = await RunComposeAsync(
                ["logs", "--no-color", "--tail", "500", "podcasts-hosting", "azurite"],
                TimeSpan.FromMinutes(2),
                throwOnFailure: false);
            var sqlLogs = await RunComposeAsync(
                ["logs", "--no-color", "--tail", "50", "sqlserver"],
                TimeSpan.FromMinutes(2),
                throwOnFailure: false);
            _output.WriteLine("Docker Compose status:");
            _output.WriteLine(ps.CombinedOutput);
            _output.WriteLine("Application and Azurite logs:");
            _output.WriteLine(applicationLogs.CombinedOutput);
            _output.WriteLine("SQL Server log tail:");
            _output.WriteLine(sqlLogs.CombinedOutput);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Could not collect Docker Compose diagnostics: {ex}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            var result = await RunComposeAsync(
                ["down", "--volumes", "--remove-orphans", "--rmi", "local", "--timeout", "10"],
                CleanupTimeout,
                throwOnFailure: false);
            if (result.ExitCode != 0)
            {
                _output.WriteLine("Docker Compose cleanup returned a non-zero exit code:");
                _output.WriteLine(result.CombinedOutput);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Docker Compose cleanup failed: {ex}");
        }
    }

    private async Task WaitUntilReadyAsync()
    {
        using var client = CreateHttpClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var deadline = DateTime.UtcNow + ReadinessTimeout;
        string? lastFailure = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync("health/ready");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }

                lastFailure = $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}";
            }
            catch (Exception ex)
            {
                lastFailure = ex.Message;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new TimeoutException($"The Compose app did not become ready. Last failure: {lastFailure}");
    }

    private Task<ProcessResult> RunComposeAsync(
        IReadOnlyCollection<string> arguments,
        TimeSpan timeout,
        bool throwOnFailure = true)
    {
        var composeArguments = new List<string>
        {
            "compose",
            "--project-name",
            ProjectName,
            "--file",
            "docker-compose.e2e.yml"
        };
        composeArguments.AddRange(arguments);

        return RunProcessAsync("docker", composeArguments, timeout, throwOnFailure);
    }

    private async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyCollection<string> arguments,
        TimeSpan timeout,
        bool throwOnFailure)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in _environmentVariables)
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Could not start process '{fileName}'.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException(
                $"Process '{fileName} {string.Join(' ', arguments)}' exceeded {timeout}.");
        }

        var result = new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
        if (throwOnFailure && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process '{fileName} {string.Join(' ', arguments)}' failed with exit code {result.ExitCode}.\n" +
                result.CombinedOutput);
        }

        return result;
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PodcastsHosting.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing PodcastsHosting.slnx.");
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => $"{StandardOutput}{Environment.NewLine}{StandardError}";
    }
}