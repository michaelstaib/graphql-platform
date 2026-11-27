namespace HotChocolate.Execution;

public class ConditionFlagsTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(63, 62)]
    [InlineData(64, 63)]
    [InlineData(130, 129)]
    public void IsSet_Should_ReturnTrue_When_FlagIsSet(int index, int neighbour)
    {
        // arrange
        var words = new ulong[3];
        words[index >> 6] = 1ul << (index & 63);
        var flags = new ConditionFlags(words[0], words[1..]);

        // act
        var isSet = flags.IsSet(index);
        var isNeighbourSet = flags.IsSet(neighbour);

        // assert
        Assert.True(isSet);
        Assert.False(isNeighbourSet);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(130)]
    public void IsSet_Should_ReturnFalse_When_OverflowIsNull(int index)
    {
        // arrange
        var flags = new ConditionFlags(ulong.MaxValue);

        // act
        var isSet = flags.IsSet(index);

        // assert
        Assert.False(isSet);
    }

    [Fact]
    public void IsSet_Should_ReturnFalse_When_IndexIsPastTheOverflowWords()
    {
        // arrange
        var flags = new ConditionFlags(ulong.MaxValue, [ulong.MaxValue]);

        // act
        var isSet = flags.IsSet(128);

        // assert
        Assert.False(isSet);
    }
}
