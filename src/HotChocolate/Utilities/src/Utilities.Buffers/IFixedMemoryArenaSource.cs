namespace HotChocolate.Buffers;

/// <summary>
/// Provides the same memory arena for every document in a result stream.
/// </summary>
internal interface IFixedMemoryArenaSource : IMemoryArenaSource;
