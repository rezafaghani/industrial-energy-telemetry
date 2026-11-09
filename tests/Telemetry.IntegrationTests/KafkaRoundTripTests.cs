using Confluent.Kafka;
using Testcontainers.Kafka;
using Xunit;

namespace Ietp.Telemetry.IntegrationTests;

public sealed class KafkaRoundTripTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:8.1.0").Build();

    public Task InitializeAsync() => _kafka.StartAsync();
    public async Task DisposeAsync() => await _kafka.DisposeAsync().AsTask();

    [Fact]
    public async Task StableKeyAndPayloadRoundTripThroughRealKafka()
    {
        const string topic = "telemetry-integration";
        var config = new ProducerConfig { BootstrapServers = _kafka.GetBootstrapAddress(), EnableIdempotence = true, Acks = Acks.All };
        using (var producer = new ProducerBuilder<string, string>(config).Build())
        {
            var delivered = await producer.ProduceAsync(topic, new Message<string, string> { Key = "series-id", Value = "payload" });
            Assert.Equal(PersistenceStatus.Persisted, delivered.Status);
        }

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress(), GroupId = Guid.NewGuid().ToString("N"), AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        consumer.Subscribe(topic);
        var consumed = consumer.Consume(TimeSpan.FromSeconds(30));
        Assert.Equal("series-id", consumed.Message.Key);
        Assert.Equal("payload", consumed.Message.Value);
    }
}
