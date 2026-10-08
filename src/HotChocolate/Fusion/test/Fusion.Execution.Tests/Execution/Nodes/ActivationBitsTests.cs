namespace HotChocolate.Fusion.Execution.Nodes;

public class ActivationBitsTests
{
    [Fact]
    public void Set_Should_SetOnlyTheGivenIndexes_When_CapacityFitsOneWord()
    {
        // arrange
        var pool = new CountingArrayPool();
        var bits = new ActivationBits(64, pool);

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
        var pool = new CountingArrayPool();
        var bits = new ActivationBits(200, pool);

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
            bits.Return(pool);
        }
    }

    [Fact]
    public void Constructor_Should_ClearTheClaimedWords_When_ThePoolReturnsDirtyMemory()
    {
        // arrange
        var pool = new CountingArrayPool();
        var dirty = new ulong[4];
        Array.Fill(dirty, ulong.MaxValue);
        pool.Seed(dirty);

        // act
        var bits = new ActivationBits(200, pool);

        // assert
        try
        {
            Assert.Same(dirty, Assert.Single(pool.Rented));
            Assert.Empty(GetSetIndexes(bits, 200));
        }
        finally
        {
            bits.Return(pool);
        }
    }

    [Fact]
    public void Return_Should_HandTheRentedWordsBackToThePool_When_CapacityExceedsOneWord()
    {
        // arrange
        var pool = new CountingArrayPool();
        var bits = new ActivationBits(200, pool);
        var words = Assert.Single(pool.Rented);

        // act
        bits.Return(pool);

        // assert
        Assert.Same(words, Assert.Single(pool.Returned));
        Assert.Equal(0, pool.Outstanding);
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
