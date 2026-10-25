namespace HotChocolate.Buffers;

/// <summary>
/// An <see cref="IMemoryArenaSource"/> whose <see cref="IMemoryArenaSource.GetNextArena"/> returns the
/// same arena for every document of a stream, so calling it does not start a new document.
/// </summary>
internal interface IFixedMemoryArenaSource : IMemoryArenaSource;
