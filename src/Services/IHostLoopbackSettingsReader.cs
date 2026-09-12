using ExWSLC.Models;

namespace ExWSLC.Services;

public interface IHostLoopbackSettingsReader
{
    Task<HostLoopbackConfiguration> ReadAsync(CancellationToken cancellationToken = default);
}
