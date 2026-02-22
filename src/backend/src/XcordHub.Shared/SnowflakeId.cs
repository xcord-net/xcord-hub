using System.Text.Json;
using System.Text.Json.Serialization;

namespace XcordHub;

public sealed class SnowflakeId
{
    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int WorkerIdBits = 10;
    private const int SequenceBits = 12;
    private const long MaxWorkerId = (1L << WorkerIdBits) - 1;
    private const long MaxSequence = (1L << SequenceBits) - 1;
    private const int WorkerIdShift = SequenceBits;
    private const int TimestampShift = SequenceBits + WorkerIdBits;

    private readonly long _workerId;
    private long _lastTimestamp = -1L;
    private long _sequence;

    public SnowflakeId(long workerId)
    {
        if (workerId is < 0 or > MaxWorkerId)
            throw new ArgumentOutOfRangeException(nameof(workerId), $"Worker ID must be between 0 and {MaxWorkerId}");

        _workerId = workerId;
    }

    public long NextId()
    {
        lock (this)
        {
            var timestamp = GetTimestamp();

            if (timestamp < _lastTimestamp)
                throw new InvalidOperationException("Clock moved backwards");

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & MaxSequence;
                if (_sequence == 0)
                    timestamp = WaitNextMillis(_lastTimestamp);
            }
            else
            {
                _sequence = 0;
            }

            _lastTimestamp = timestamp;

            return (timestamp << TimestampShift) | (_workerId << WorkerIdShift) | _sequence;
        }
    }

    private static long GetTimestamp() => (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;

    private static long WaitNextMillis(long lastTimestamp)
    {
        var timestamp = GetTimestamp();
        while (timestamp <= lastTimestamp)
            timestamp = GetTimestamp();
        return timestamp;
    }

    public static DateTime GetTimestampFromId(long id)
    {
        var timestamp = id >> TimestampShift;
        return Epoch.AddMilliseconds(timestamp);
    }

    public static long GetWorkerIdFromId(long id)
    {
        return (id >> WorkerIdShift) & MaxWorkerId;
    }

    public static long GetSequenceFromId(long id)
    {
        return id & MaxSequence;
    }
}

public sealed class SnowflakeJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (long.TryParse(stringValue, out var value))
                return value;
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt64();
        }

        throw new JsonException("Invalid snowflake ID format");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
