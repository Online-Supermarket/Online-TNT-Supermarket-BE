using Confluent.Kafka;

internal static class KafkaClientSettings
{
    internal static ProducerConfig Producer(IConfiguration configuration)
    {
        var settings = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            MessageTimeoutMs = 3000
        };
        ApplyAuthentication(settings, configuration);
        return settings;
    }

    internal static AdminClientConfig Admin(IConfiguration configuration)
    {
        var settings = new AdminClientConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            SocketTimeoutMs = 2000
        };
        ApplyAuthentication(settings, configuration);
        return settings;
    }

    private static void ApplyAuthentication(ClientConfig settings, IConfiguration configuration)
    {
        var protocol = configuration["Kafka:SecurityProtocol"];
        if (string.IsNullOrWhiteSpace(protocol)) return;

        settings.SecurityProtocol = Enum.Parse<SecurityProtocol>(protocol, ignoreCase: true);
        var mechanism = configuration["Kafka:SaslMechanism"];
        if (!string.IsNullOrWhiteSpace(mechanism))
            settings.SaslMechanism = Enum.Parse<SaslMechanism>(mechanism, ignoreCase: true);
        settings.SaslUsername = configuration["Kafka:SaslUsername"];
        settings.SaslPassword = configuration["Kafka:SaslPassword"];
    }
}
