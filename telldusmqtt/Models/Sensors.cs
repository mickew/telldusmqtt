using System.Text.Json.Serialization;

namespace telldusmqtt.Models;

// Root myDeserializedClass = JsonSerializer.Deserialize<Root>(myJsonResponse);
internal class Sensors
{
    [JsonPropertyName("sensor")]
    public required List<Sensor> Sensor { get; set; }
}

internal class Sensor
{
    [JsonPropertyName("battery")]
    public int Battery { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("novalues")]
    public bool Novalues { get; set; }

    [JsonPropertyName("protocol")]
    public required string Protocol { get; set; }

    [JsonPropertyName("sensorId")]
    public int SensorId { get; set; }

    public List<Data> Data { get; set; } = new List<Data>();
}

internal class Data
{
    [JsonPropertyName("lastUpdated")]
    public int LastUpdated { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("scale")]
    public int Scale { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }
}

internal record SensorData(
    [property: JsonPropertyName("battery")] int Battery,
    [property: JsonPropertyName("data")] List<Data> Data,
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("lastUpdated")] int LastUpdated,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("sensorId")] int SensorId
);
