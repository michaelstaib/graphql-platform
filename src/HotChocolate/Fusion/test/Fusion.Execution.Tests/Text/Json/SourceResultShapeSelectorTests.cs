using System.Buffers;
using System.Text;

namespace HotChocolate.Fusion.Text.Json;

public class SourceResultShapeSelectorTests
{
    [Theory]
    [InlineData("""{"requestIndex":0,"data":{"a":1}}""", 0)]
    [InlineData("""{"requestIndex":1,"variableIndex":3,"data":{"a":1}}""", 1)]
    [InlineData("""{"requestIndex":2}""", 2)]
    [InlineData("""  { "requestIndex" : 1 , "data" : { "a" : 1 } }""", 1)]
    [InlineData("{\t\"requestIndex\"\t:\t1\t}", 1)]
    [InlineData("""{"requestIndex":999999999,"data":null}""", 999999999)]
    public void TryReadRequestIndex_Should_ReturnIndex_When_RequestIndexIsTheFirstProperty(
        string record,
        int expectedIndex)
    {
        // arrange
        var sequence = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(record));

        // act
        var success = SourceResultShapeSelector.TryReadRequestIndex(in sequence, out var index);

        // assert
        Assert.True(success);
        Assert.Equal(expectedIndex, index);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("""[{"requestIndex":1}]""")]
    [InlineData("""{"data":{"a":1},"requestIndex":1}""")]
    [InlineData("""{"variableIndex":1,"requestIndex":1}""")]
    [InlineData("""{"requestIndex":-1,"data":null}""")]
    [InlineData("""{"requestIndex":1.0,"data":null}""")]
    [InlineData("""{"requestIndex":1e2,"data":null}""")]
    [InlineData("""{"requestIndex":01,"data":null}""")]
    [InlineData("""{"requestIndex":"1","data":null}""")]
    [InlineData("""{"requestIndex":null,"data":null}""")]
    [InlineData("""{"requestIndex":1000000000,"data":null}""")]
    [InlineData("""{"requestIndex":1""")]
    [InlineData("""{"requestIndex":""")]
    [InlineData("""{"requestIndex" """)]
    [InlineData("""{"requestIndexes":1}""")]
    [InlineData("""{"RequestIndex":1}""")]
    public void TryReadRequestIndex_Should_ReturnFalse_When_RecordDoesNotStartWithAnIntegerRequestIndex(
        string record)
    {
        // arrange
        var sequence = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(record));

        // act
        var success = SourceResultShapeSelector.TryReadRequestIndex(in sequence, out _);

        // assert
        Assert.False(success);
    }

    [Fact]
    public void TryReadRequestIndex_Should_ReturnFalse_When_NameHasAnEscapeSequence()
    {
        // arrange
        // the name requestIndex with its first letter as an escape sequence
        var record = "{\"" + (char)92 + "u0072equestIndex\":1}";
        var sequence = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(record));

        // act
        var success = SourceResultShapeSelector.TryReadRequestIndex(in sequence, out _);

        // assert
        Assert.False(success);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    public void TryReadRequestIndex_Should_ReturnIndex_When_RecordIsSplitIntoSegments(int segmentLength)
    {
        // arrange
        var record = Encoding.UTF8.GetBytes("""{"requestIndex":12,"data":{"user":{"id":1}}}""");
        var sequence = CreateSequence(record, segmentLength);

        // act
        var success = SourceResultShapeSelector.TryReadRequestIndex(in sequence, out var index);

        // assert
        Assert.True(success);
        Assert.Equal(12, index);
    }

    [Fact]
    public void TryReadRequestIndex_Should_ReturnFalse_When_IndexDoesNotEndWithinTheReadPrefix()
    {
        // arrange
        // the whitespace moves the index beyond the bytes that are read of a record in segments
        var record = Encoding.UTF8.GetBytes("{" + new string(' ', 80) + "\"requestIndex\":1}");
        var sequence = CreateSequence(record, segmentLength: 8);

        // act
        var success = SourceResultShapeSelector.TryReadRequestIndex(in sequence, out _);

        // assert
        Assert.False(success);
    }

    [Theory]
    [InlineData("""{"requestIndex":0,"data":null}""", 0)]
    [InlineData("""{"requestIndex":1,"data":null}""", 1)]
    [InlineData("""{"requestIndex":2,"data":null}""", -1)]
    [InlineData("""{"requestIndex":3,"data":null}""", -1)]
    [InlineData("""{"variableIndex":1,"data":null}""", -1)]
    public void GetShape_Should_ReturnTheShapeAtTheRequestIndex_When_ShapesAreGivenByPosition(
        string record,
        int expectedShape)
    {
        // arrange
        // the third shape lies beyond the count and the last position has no shape
        SourceResultShape?[] shapes = [CreateShape("a"), CreateShape("b"), CreateShape("c"), null];
        var selector = new SourceResultShapeSelector(shapes, count: 2);
        var sequence = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(record));

        // act
        var shape = selector.GetShape(in sequence);

        // assert
        Assert.Same(expectedShape < 0 ? null : shapes[expectedShape], shape);
    }

    [Theory]
    [InlineData("""{"requestIndex":1,"data":null}""")]
    [InlineData("""{"variableIndex":1,"data":null}""")]
    [InlineData("""{"data":null}""")]
    public void GetShape_Should_ReturnTheShape_When_OneShapeIsExpectedOfEveryResult(string record)
    {
        // arrange
        var expected = CreateShape("a");
        var selector = new SourceResultShapeSelector(expected);
        var sequence = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(record));

        // act
        var shape = selector.GetShape(in sequence);

        // assert
        Assert.Same(expected, shape);
    }

    [Fact]
    public void GetShape_Should_ReturnNull_When_SelectorIsDefault()
    {
        // arrange
        var selector = default(SourceResultShapeSelector);
        var sequence = new ReadOnlySequence<byte>("""{"requestIndex":0,"data":null}"""u8.ToArray());

        // act
        var shape = selector.GetShape(in sequence);

        // assert
        Assert.Null(shape);
    }

    private static SourceResultShape CreateShape(string fieldName)
    {
        var builder = new SourceResultShape.Builder();
        var data = builder.AddObject([fieldName], [SourceResultShape.NoNode], identify: true);
        return builder.Build(builder.AddObject(["data"], [data], identify: false));
    }

    private static ReadOnlySequence<byte> CreateSequence(byte[] data, int segmentLength)
    {
        var first = new Segment(data.AsMemory(0, Math.Min(segmentLength, data.Length)));
        var last = first;

        for (var offset = segmentLength; offset < data.Length; offset += segmentLength)
        {
            last = last.Append(data.AsMemory(offset, Math.Min(segmentLength, data.Length - offset)));
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
