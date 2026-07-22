using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using Quartz;
using telldusmqtt.Models;
using telldusmqtt.Services;

namespace telldusmqtt.Jobs;

internal class GetTelldusStatusJob : IJob
{
    private readonly ILogger<GetTelldusStatusJob> _logger;
    private readonly ITelldusClientService _telldusClientService;

    public GetTelldusStatusJob(ILogger<GetTelldusStatusJob> logger, ITelldusClientService telldusClientService)
    {
        _logger = logger;
        _telldusClientService = telldusClientService;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogDebug("Executing GetTelldusStatusJob at {Time}", DateTimeOffset.Now);

        // Call the TelldusClientService to get the status
        var (lastReadingOk, sensors) = await GetTelldusStatusAsync(context.CancellationToken);

        _logger.LogDebug("Telldus status: {Status}", sensors);
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
