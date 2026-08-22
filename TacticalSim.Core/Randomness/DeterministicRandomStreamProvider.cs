using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TacticalSim.Core.Randomness
{
    /// <summary>
    /// Creates stable named streams using FNV-1a name hashing and SplitMix64 generation.
    /// </summary>
    public sealed class DeterministicRandomStreamProvider : IDeterministicRandomStreamProvider
    {
        /// <summary>
        /// Identifies the exact derivation and generation algorithm used by snapshots.
        /// </summary>
        public const string CurrentAlgorithmVersion = "fnv1a64-splitmix64-v1";

        // Published FNV-1a 64-bit offset basis and prime.
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private readonly object _gate = new();
        private readonly Dictionary<string, SplitMix64RandomSource> _streams = new(StringComparer.Ordinal);
        private readonly ulong _rootSeed;

        /// <summary>
        /// Initializes a provider using an injected scenario/replay root seed.
        /// </summary>
        public DeterministicRandomStreamProvider(IRootSeedProvider rootSeedProvider)
        {
            ArgumentNullException.ThrowIfNull(rootSeedProvider);
            _rootSeed = rootSeedProvider.RootSeed;
        }

        /// <inheritdoc />
        public ulong RootSeed => _rootSeed;

        /// <inheritdoc />
        public IDeterministicRandomSource GetStream(string streamName)
        {
            ArgumentNullException.ThrowIfNull(streamName);

            if (string.IsNullOrWhiteSpace(streamName))
            {
                throw new ArgumentException("A deterministic random stream name cannot be empty or whitespace.", nameof(streamName));
            }

            if (!string.Equals(streamName, streamName.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("A deterministic random stream name cannot have leading or trailing whitespace.", nameof(streamName));
            }

            lock (_gate)
            {
                if (!_streams.TryGetValue(streamName, out var stream))
                {
                    stream = new SplitMix64RandomSource(
                        _gate,
                        streamName,
                        DeriveStreamSeed(RootSeed, streamName));
                    _streams.Add(streamName, stream);
                }

                return stream;
            }
        }

        /// <inheritdoc />
        public DeterministicRandomMetadataSnapshot CaptureSnapshot()
        {
            lock (_gate)
            {
                var metadata = _streams.Values
                    .OrderBy(stream => stream.StreamName, StringComparer.Ordinal)
                    .Select(stream => stream.CaptureMetadata())
                    .ToArray();

                return new DeterministicRandomMetadataSnapshot(
                    CurrentAlgorithmVersion,
                    RootSeed,
                    Array.AsReadOnly(metadata));
            }
        }

        private static ulong DeriveStreamSeed(ulong rootSeed, string streamName)
        {
            ulong nameHash = FnvOffsetBasis;
            foreach (byte value in Encoding.UTF8.GetBytes(streamName))
            {
                nameHash ^= value;
                nameHash = unchecked(nameHash * FnvPrime);
            }

            return SplitMix64RandomSource.Mix(rootSeed ^ nameHash);
        }

        private sealed class SplitMix64RandomSource : IDeterministicRandomSource
        {
            // Published SplitMix64 increment and avalanche constants.
            private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
            private const ulong MixMultiplier1 = 0xBF58476D1CE4E5B9UL;
            private const ulong MixMultiplier2 = 0x94D049BB133111EBUL;

            // A binary64 has 53 bits of integer precision; scaling those bits gives [0, 1).
            private const double InverseTwoToThe53 = 1.0 / 9007199254740992.0;

            private readonly object _gate;
            private ulong _state;
            private ulong _drawCount;

            internal SplitMix64RandomSource(object gate, string streamName, ulong streamSeed)
            {
                _gate = gate;
                StreamName = streamName;
                StreamSeed = streamSeed;
                _state = streamSeed;
            }

            public string StreamName { get; }

            public ulong StreamSeed { get; }

            public ulong DrawCount
            {
                get
                {
                    lock (_gate)
                    {
                        return _drawCount;
                    }
                }
            }

            public ulong NextUInt64()
            {
                lock (_gate)
                {
                    _state = unchecked(_state + GoldenGamma);
                    _drawCount++;
                    return Mix(_state);
                }
            }

            public double NextUnitDouble()
            {
                return (NextUInt64() >> 11) * InverseTwoToThe53;
            }

            internal DeterministicRandomStreamMetadata CaptureMetadata()
            {
                lock (_gate)
                {
                    return new DeterministicRandomStreamMetadata(StreamName, StreamSeed, _drawCount);
                }
            }

            internal static ulong Mix(ulong value)
            {
                value = unchecked((value ^ (value >> 30)) * MixMultiplier1);
                value = unchecked((value ^ (value >> 27)) * MixMultiplier2);
                return value ^ (value >> 31);
            }
        }
    }
}
