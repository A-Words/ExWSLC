using System.Globalization;
using System.Text.Json;
using ExWSLC.Helpers;
using ExWSLC.Models;

namespace ExWSLC.Services;

public sealed partial class WslcContainerRuntime
{
    private readonly IHostLoopbackSettingsReader _hostLoopbackSettings = hostLoopbackSettings ?? new HostLoopbackSettingsReader();

    public Task<HostLoopbackConfiguration> GetHostLoopbackConfigurationAsync(CancellationToken cancellationToken = default) =>
        _hostLoopbackSettings.ReadAsync(cancellationToken);

    public async Task<HostLoopbackProbeResult> ProbeHostLoopbackAsync(HostLoopbackProbeRequest target, RuntimeCapabilities capabilities, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var dnsSucceeded = false;
        HostLoopbackProbeResult Result(HostLoopbackOutcome outcome) => new(target, started, outcome, dnsSucceeded,
            RuntimeSystemInfoParser.SafeVersion(capabilities.CliVersion));

        if (!HostLoopbackTargetValidator.IsContainerId(target.ContainerId) || !HostLoopbackTargetValidator.IsPort(target.Port) ||
            !HostLoopbackTargetValidator.IsHostName(target.HostName)) return Result(HostLoopbackOutcome.InvalidTarget);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (capabilities.CliAvailability == CapabilitySupport.Unsupported)
                return Result(HostLoopbackOutcome.CapabilityUnknown);
            if (capabilities[RuntimeFeature.HostLoopback].Support == CapabilitySupport.Unsupported)
                return Result(HostLoopbackOutcome.CapabilityDisabled);
            var configuration = await GetHostLoopbackConfigurationAsync(timeout.Token);
            if (configuration.Support == CapabilitySupport.Unsupported) return Result(HostLoopbackOutcome.CapabilityDisabled);
            if (configuration.HostName.Length == 0) return Result(HostLoopbackOutcome.CapabilityUnknown);
            if (!target.HostName.Equals(configuration.HostName, StringComparison.OrdinalIgnoreCase))
                return Result(HostLoopbackOutcome.ConfigurationChanged);

            var state = await ReadProbeContainerAsync(target.ContainerId, timeout.Token);
            if (state.Outcome is { } unavailable) return Result(unavailable);
            // Bind exec to the canonical ID returned by the targeted inspect, not a name.
            var result = await RunAsync(BuildHostLoopbackArguments(state.Id, target.HostName, target.Port), cancellationToken: timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            var markers = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            dnsSucceeded = markers.Contains("EXWSLC09:DNS_OK");
            if (markers.Contains("EXWSLC09:TIMEOUT")) return Result(HostLoopbackOutcome.TimedOut);
            if (result.Success)
            {
                if (markers.SequenceEqual(new[] { "EXWSLC09:DNS_OK", "EXWSLC09:TCP_OK" })) return Result(HostLoopbackOutcome.Connected);
                if (markers.SequenceEqual(new[] { "EXWSLC09:DNS_FAIL" })) return Result(HostLoopbackOutcome.DnsFailed);
                if (markers.SequenceEqual(new[] { "EXWSLC09:DNS_OK", "EXWSLC09:TCP_FAIL" })) return Result(HostLoopbackOutcome.TcpFailed);
                if (markers.SequenceEqual(new[] { "EXWSLC09:TOOLS_MISSING" })) return Result(HostLoopbackOutcome.ToolsMissing);
            }
            // Exec may race with a user stopping/removing the container elsewhere.
            state = await ReadProbeContainerAsync(state.Id, timeout.Token);
            return Result(state.Outcome ?? (!result.Success ? HostLoopbackOutcome.ToolsMissing : HostLoopbackOutcome.RuntimeFailed));
        }
        catch (OperationCanceledException)
        {
            return Result(cancellationToken.IsCancellationRequested ? HostLoopbackOutcome.Cancelled : HostLoopbackOutcome.TimedOut);
        }
        catch (Exception)
        {
            return Result(cancellationToken.IsCancellationRequested ? HostLoopbackOutcome.Cancelled : HostLoopbackOutcome.RuntimeFailed);
        }
    }

    private async Task<(string Id, HostLoopbackOutcome? Outcome)> ReadProbeContainerAsync(string id, CancellationToken token)
    {
        var result = await RunAsync(["container", "inspect", id, "--format", "json"], cancellationToken: token);
        token.ThrowIfCancellationRequested();
        if (!result.Success) return (id, HostLoopbackOutcome.ContainerUnavailable);
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 1) root = root[0];
            var canonicalId = root.ReadString("Id", "ID");
            if (!HostLoopbackTargetValidator.IsContainerId(canonicalId) || !canonicalId.StartsWith(id, StringComparison.OrdinalIgnoreCase))
                return (id, HostLoopbackOutcome.ContainerUnavailable);
            if (!root.TryGetPropertyIgnoreCase("State", out var state) || state.ValueKind != JsonValueKind.Object)
                return (canonicalId, HostLoopbackOutcome.RuntimeFailed);
            if ((state.TryGetPropertyIgnoreCase("Paused", out var paused) && paused.ValueKind == JsonValueKind.True) ||
                (state.TryGetPropertyIgnoreCase("Restarting", out var restarting) && restarting.ValueKind == JsonValueKind.True))
                return (canonicalId, HostLoopbackOutcome.ContainerNotRunning);
            if (state.TryGetPropertyIgnoreCase("Running", out var running) && running.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return (canonicalId, running.GetBoolean() ? null : HostLoopbackOutcome.ContainerNotRunning);
            return (canonicalId, HostLoopbackOutcome.RuntimeFailed);
        }
        catch (JsonException) { return (id, HostLoopbackOutcome.RuntimeFailed); }
    }

    internal static IReadOnlyList<string> BuildHostLoopbackArguments(string containerId, string hostName, int port) =>
        ["exec", containerId, "/bin/sh", "-c", HostProbeScript.Replace("\r\n", "\n"), "exwslc-host-probe", hostName,
            port.ToString(CultureInfo.InvariantCulture), "exec 3<>/dev/tcp/\"$1\"/\"$2\" || exit 1; exec 3>&-; exec 3<&-"];

    // User values are separate argv entries, referenced only as quoted shell arguments.
    // Python isolated mode ignores PYTHON* environment variables and user startup code.
    // SIGALRM bounds DNS too; closing/killing the CLI cannot leave a long-running probe.
    private const string HostProbeScript = """
        unset BASH_ENV ENV
        if command -v python3 >/dev/null 2>&1; then
        exec python3 -I -S -c '
        import signal, socket, sys
        def expired(*args):
            print("EXWSLC09:TIMEOUT", flush=True)
            sys.exit(124)
        signal.signal(signal.SIGALRM, expired)
        signal.alarm(8)
        try:
            addresses = socket.getaddrinfo(sys.argv[1], int(sys.argv[2]), socket.AF_INET, socket.SOCK_STREAM)
        except socket.gaierror:
            print("EXWSLC09:DNS_FAIL", flush=True)
            sys.exit(0)
        if not addresses:
            print("EXWSLC09:DNS_FAIL", flush=True)
            sys.exit(0)
        print("EXWSLC09:DNS_OK", flush=True)
        family, kind, protocol, _, address = addresses[0]
        try:
            with socket.socket(family, kind, protocol) as connection:
                connection.settimeout(3)
                connection.connect(address)
            print("EXWSLC09:TCP_OK", flush=True)
        except OSError:
            print("EXWSLC09:TCP_FAIL", flush=True)
        ' "$1" "$2"
        fi
        if command -v bash >/dev/null 2>&1 && command -v getent >/dev/null 2>&1 && command -v timeout >/dev/null 2>&1; then
            timeout -s KILL 8 bash -c '
                resolved=$(timeout -s KILL 3 getent ahostsv4 "$1" 2>/dev/null)
                code=$?
                if [ "$code" -eq 124 ] || [ "$code" -eq 137 ]; then printf "EXWSLC09:TIMEOUT\n"; exit 0; fi
                if [ "$code" -eq 1 ] || [ "$code" -eq 3 ] || [ "$code" -ge 125 ]; then printf "EXWSLC09:TOOLS_MISSING\n"; exit 0; fi
                if [ "$code" -ne 0 ] || [ -z "$resolved" ]; then printf "EXWSLC09:DNS_FAIL\n"; exit 0; fi
                read -r address rest <<< "$resolved"
                if ! [[ "$address" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then printf "EXWSLC09:DNS_FAIL\n"; exit 0; fi
                printf "EXWSLC09:DNS_OK\n"
                if timeout -s KILL 3 bash -c "$3" exwslc-tcp "$address" "$2" >/dev/null 2>&1; then
                    printf "EXWSLC09:TCP_OK\n"
                else
                    printf "EXWSLC09:TCP_FAIL\n"
                fi
            ' exwslc-dns "$1" "$2" "$3"
            code=$?
            if [ "$code" -eq 124 ] || [ "$code" -eq 137 ]; then printf 'EXWSLC09:TIMEOUT\n'; fi
            exit "$code"
        fi
        printf 'EXWSLC09:TOOLS_MISSING\n'
        """;
}
