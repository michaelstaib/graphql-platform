using System.Security.Cryptography;

namespace HotChocolate.Fusion.Utilities;

public class Sha256DigestTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(55)]
    [InlineData(56)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(119)]
    [InlineData(120)]
    [InlineData(128)]
    [InlineData(1000)]
    [InlineData(70000)]
    public void Compute_Should_MatchPlatformDigest_When_SourceHasLength(int length)
    {
        // arrange
        var source = new byte[length];
        new Random(length).NextBytes(source);
        var expected = SHA256.HashData(source);
        var actual = new byte[Sha256Digest.SizeInBytes];

        // act
        Sha256Digest.Compute(source, actual);

        // assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Compute_Should_LeaveBytesAfterTheDigest_When_DestinationIsLonger()
    {
        // arrange
        var source = "abc"u8.ToArray();
        var destination = new byte[Sha256Digest.SizeInBytes + 4];
        destination.AsSpan(Sha256Digest.SizeInBytes).Fill(0xAB);

        // act
        Sha256Digest.Compute(source, destination);

        // assert
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Convert.ToHexStringLower(destination.AsSpan(0, Sha256Digest.SizeInBytes)));
        Assert.Equal([0xAB, 0xAB, 0xAB, 0xAB], destination[Sha256Digest.SizeInBytes..]);
    }
}
