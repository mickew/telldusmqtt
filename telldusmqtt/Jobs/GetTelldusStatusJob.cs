using Microsoft.Extensions.Logging;
using Quartz;
using telldusmqtt.Models;
using telldusmqtt.Services;

namespace telldusmqtt.Jobs;

internal class GetTelldusStatusJob : IJob
{
    private readonly ILogger<GetTelldusStatusJob> _logger;
    private readonly ITelldusClientService _telldusClientService;
    private readonly IMqttClientService _mqttClientService;

    public GetTelldusStatusJob(ILogger<GetTelldusStatusJob> logger, ITelldusClientService telldusClientService, IMqttClientService mqttClientService)
    {
        _logger = logger;
        _telldusClientService = telldusClientService;
        _mqttClientService = mqttClientService;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing GetTelldusStatusJob at {Time}", DateTimeOffset.Now);

        // Call the TelldusClientService to get the status
        var (lastReadingOk, sensors) = await GetTelldusStatusAsync(cancellationToken);

        if (lastReadingOk)
        {
            if (await _mqttClientService.IsConnectedAsync)
            {
                await _mqttClientService.PublishStatusAsync(sensors);
            }
            else
            {
                _logger.LogWarning("MQTT client is not connected. Skipping publish.");
            }
        }
    }

    private async Task<(bool lastReadingOk, Sensors sensors)> GetTelldusStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _telldusClientService.GetSensorsStatusAsync(cancellationToken);
            return (true, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Telldus status");
            return (false, new Sensors() { Sensor = new List<Sensor>() });
        }
    }
}
