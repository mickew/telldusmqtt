using Microsoft.Extensions.Hosting;
using telldusmqtt.Models;

namespace telldusmqtt.Services;

internal interface ITelldusClientService : IHostedService
{
    Task<Sensors> GetSensorsStatusAsync(CancellationToken cancellationToken);
}
