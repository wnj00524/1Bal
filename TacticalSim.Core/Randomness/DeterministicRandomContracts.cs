using System;
using System.Collections.Generic;

namespace TacticalSim.Core.Randomness
{
    /// <summary>
    /// Supplies the recorded root seed from which all simulation random streams are derived.
    /// </summary>
    public interface IRootSeedProvider
    {
        /// <summary>
        /// Gets the root seed for the current scenario or replay.
        /// </summary>
        ulong RootSeed { get; }
    }

    /// <summary>
    /// Immutable root-seed provider for a scenario or replay.
    /// </summary>
    public sealed class FixedRootSeedProvider : IRootSeedProvider
    {
        /// <summary>
        /// Initializes a provider with an explicitly recorded root seed.
        /// </summary>
        public FixedRootSeedProvider(ulong rootSeed)
        {
            RootSeed = rootSeed;
        }

        /// <inheritdoc />
        public ulong RootSeed { get; }
    }

    /// <summary>
    /// A deterministic source belonging to one stable, named simulation stream.
    /// </summary>
    public interface IDeterministicRandomSource
    {
        /// <summary>
        /// Gets the stable, ordinal stream name.
        /// </summary>
        string StreamName { get; }

        /// <summary>
        /// Gets the seed derived from the root seed and stream name.
        /// </summary>
        ulong StreamSeed { get; }

        /// <summary>
        /// Gets the number of values drawn from this stream.
        /// </summary>
        ulong DrawCount { get; }

        /// <summary>
        /// Returns the next unsigned 64-bit value in the stream.
        /// </summary>
        ulong NextUInt64();

        /// <summary>
        /// Returns the next value in the half-open interval [0, 1).
        /// </summary>
        double NextUnitDouble();
    }

    /// <summary>
    /// Provides independently advancing deterministic streams by stable name.
    /// </summary>
    public interface IDeterministicRandomStreamProvider
    {
        /// <summary>
        /// Gets the root seed used to derive named streams.
        /// </summary>
        ulong RootSeed { get; }

        /// <summary>
        /// Gets or creates the stream with the supplied stable name.
        /// </summary>
        IDeterministicRandomSource GetStream(string streamName);

        /// <summary>
        /// Captures replay/debug metadata without advancing any stream.
        /// </summary>
        DeterministicRandomMetadataSnapshot CaptureSnapshot();
    }

    /// <summary>
    /// Replay/debug metadata for one named deterministic stream.
    /// </summary>
    public sealed record DeterministicRandomStreamMetadata(
        string StreamName,
        ulong StreamSeed,
        ulong DrawCount);

    /// <summary>
    /// Replay/debug metadata for the root seed and every stream created so far.
    /// </summary>
    public sealed record DeterministicRandomMetadataSnapshot(
        string AlgorithmVersion,
        ulong RootSeed,
        IReadOnlyList<DeterministicRandomStreamMetadata> Streams);
}
