using Microsoft.Extensions.Hosting;
using telldusmqtt.Models;

namespace telldusmqtt.Services;

internal interface IMqttClientService : IHostedService
{
    Task<bool> IsConnectedAsync { get; }

    Task<bool> PublishStatusAsync(Sensors sensorsStatus);
}
