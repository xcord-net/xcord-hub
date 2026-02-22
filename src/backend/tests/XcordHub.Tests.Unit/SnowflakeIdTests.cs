using FluentAssertions;
using XcordHub;

namespace XcordHub.Tests.Unit;

public sealed class SnowflakeIdTests
{
    [Fact]
    public void NextId_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeId(workerId: 1);

        // Act
        var id1 = generator.NextId();
        var id2 = generator.NextId();

        // Assert
        id1.Should().NotBe(id2);
        id2.Should().BeGreaterThan(id1);
    }

    [Fact]
    public void NextId_ShouldGenerateMultipleUniqueIdsInSequence()
    {
        // Arrange
        var generator = new SnowflakeId(workerId: 5);
        var ids = new HashSet<long>();

        // Act
        for (int i = 0; i < 1000; i++)
        {
            ids.Add(generator.NextId());
        }

        // Assert
        ids.Should().HaveCount(1000);
    }

    [Fact]
    public void Constructor_ShouldThrowForInvalidWorkerId()
    {
        // Act & Assert
        Action actNegative = () => new SnowflakeId(workerId: -1);
        Action actTooLarge = () => new SnowflakeId(workerId: 1024);

        actNegative.Should().Throw<ArgumentOutOfRangeException>();
        actTooLarge.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NextId_ShouldGenerateMonotonicallyIncreasingIds()
    {
        // Arrange
        var generator = new SnowflakeId(workerId: 10);
        var previousId = 0L;

        // Act & Assert
        for (int i = 0; i < 100; i++)
        {
            var id = generator.NextId();
            id.Should().BeGreaterThan(previousId);
            previousId = id;
        }
    }

    [Fact]
    public void GetWorkerIdFromId_ShouldExtractWorkerIdCorrectly()
    {
        // Arrange
        const long workerId = 42;
        var generator = new SnowflakeId(workerId: workerId);

        // Act
        var id = generator.NextId();
        var extractedWorkerId = SnowflakeId.GetWorkerIdFromId(id);

        // Assert
        extractedWorkerId.Should().Be(workerId);
    }

    [Fact]
    public void GetTimestampFromId_ShouldExtractTimestampCorrectly()
    {
        // Arrange
        var generator = new SnowflakeId(workerId: 1);
        var beforeGeneration = DateTime.UtcNow;

        // Act
        var id = generator.NextId();
        var afterGeneration = DateTime.UtcNow;
        var extractedTimestamp = SnowflakeId.GetTimestampFromId(id);

        // Assert
        extractedTimestamp.Should().BeOnOrAfter(beforeGeneration.AddMilliseconds(-1));
        extractedTimestamp.Should().BeOnOrBefore(afterGeneration.AddMilliseconds(1));
    }

    [Fact]
    public void GetSequenceFromId_ShouldExtractSequenceCorrectly()
    {
        // Arrange
        var generator = new SnowflakeId(workerId: 1);

        // Generate two IDs in the same millisecond — call them back-to-back so the
        // timestamp doesn't advance between calls. The first ID in a new millisecond
        // always gets sequence 0; the second (within the same ms) gets sequence 1.
        // Generate a burst of IDs and find the first adjacent pair that share the same
        // millisecond timestamp, then verify their sequences are 0 and 1.
        var ids = new long[100];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = generator.NextId();
        }

        // Find the first pair of adjacent IDs with the same timestamp (same millisecond)
        long? firstId = null;
        long? secondId = null;
        for (int i = 0; i < ids.Length - 1; i++)
        {
            var ts1 = SnowflakeId.GetTimestampFromId(ids[i]);
            var ts2 = SnowflakeId.GetTimestampFromId(ids[i + 1]);
            if (ts1 == ts2)
            {
                firstId = ids[i];
                secondId = ids[i + 1];
                break;
            }
        }

        // Assert - if we found a same-millisecond pair, verify the sequences are consecutive
        firstId.Should().NotBeNull("expected to find at least two IDs generated in the same millisecond out of 100");
        var seq1 = SnowflakeId.GetSequenceFromId(firstId!.Value);
        var seq2 = SnowflakeId.GetSequenceFromId(secondId!.Value);
        seq2.Should().Be(seq1 + 1, "consecutive IDs within the same millisecond should have consecutive sequence numbers");
    }

    [Fact]
    public void DifferentGenerators_ShouldProduceDifferentIds()
    {
        // Arrange
        var generator1 = new SnowflakeId(workerId: 1);
        var generator2 = new SnowflakeId(workerId: 2);

        // Act
        var id1 = generator1.NextId();
        var id2 = generator2.NextId();

        // Assert
        id1.Should().NotBe(id2);
        SnowflakeId.GetWorkerIdFromId(id1).Should().Be(1);
        SnowflakeId.GetWorkerIdFromId(id2).Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1023)]
    public void GetWorkerIdFromId_ShouldWorkForAllValidWorkerIds(long workerId)
    {
        // Arrange
        var generator = new SnowflakeId(workerId: workerId);

        // Act
        var id = generator.NextId();
        var extractedWorkerId = SnowflakeId.GetWorkerIdFromId(id);

        // Assert
        extractedWorkerId.Should().Be(workerId);
    }

    [Fact]
    public void RapidGeneration_ShouldMaintainUniqueness()
    {
        // Arrange
        var generator = new SnowflakeId(workerId: 7);
        var ids = new HashSet<long>();

        // Act
        for (int i = 0; i < 10000; i++)
        {
            ids.Add(generator.NextId());
        }

        // Assert
        ids.Should().HaveCount(10000, "all generated IDs should be unique");
    }
}
