using System.Buffers;

namespace HotChocolate.Fusion.Execution.Nodes;

public class ActivationBitsTests
{
    [Fact]
    public void Set_Should_SetOnlyTheGivenIndexes_When_CapacityFitsOneWord()
    {
        // arrange
        var bits = new ActivationBits(64);

        // act
        bits.Set(0);
        bits.Set(5);
        bits.Set(63);

        // assert
        Assert.Equal([0, 5, 63], GetSetIndexes(bits, 64));
    }

    [Fact]
    public void Set_Should_SetOnlyTheGivenIndexes_When_CapacityExceedsOneWord()
    {
        // arrange
        var bits = new ActivationBits(200);

        try
        {
            // act
            foreach (var index in new[] { 0, 63, 64, 127, 128, 199 })
            {
                bits.Set(index);
            }

            // assert
            Assert.Equal([0, 63, 64, 127, 128, 199], GetSetIndexes(bits, 200));
        }
        finally
        {
            bits.Return();
        }
    }

    [Fact]
    public void Constructor_Should_ClearTheRentedWords_When_ThePoolReturnsDirtyMemory()
    {
        // arrange
        var dirty = new ActivationBits(200);
        dirty.Set(100);
        dirty.Set(190);
        var words = dirty.RentedWords!;
        var dirtyWordCount = words.Count(word => word != 0);
        dirty.Return();

        // act
        var bits = new ActivationBits(200);

        // assert
        try
        {
            Assert.Equal(2, dirtyWordCount);
            Assert.Same(words, bits.RentedWords);
            Assert.Empty(GetSetIndexes(bits, 200));
        }
        finally
        {
            bits.Return();
        }
    }

    [Fact]
    public void Return_Should_ReleaseTheRentedWords_When_CapacityExceedsOneWord()
    {
        // arrange
        var bits = new ActivationBits(200);
        var words = bits.RentedWords!;

        // act
        bits.Return();

        // assert
        Assert.Same(words, ArrayPool<ulong>.Shared.Rent(words.Length));
        Assert.Null(bits.RentedWords);
    }

    private static int[] GetSetIndexes(ActivationBits bits, int capacity)
    {
        var indexes = new List<int>();

        for (var i = 0; i < capacity; i++)
        {
            if (bits.Get(i))
            {
                indexes.Add(i);
            }
        }

        return [.. indexes];
    }
}
