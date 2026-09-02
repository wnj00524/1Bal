using System.Numerics;

namespace ProxyState.Simulation;

// Dense immutable bitsets keep catalogue-scale work out of decision ticks. The
// runtime index assigned by IntentCompiler is also the bit position, so no hash
// lookup or string comparison is needed while generating candidates.
public sealed class IntentBitSet
{
    private readonly ulong[] _words;

    internal IntentBitSet(int capacity, ulong[] words)
    {
        Capacity = capacity;
        _words = words;
    }

    public int Capacity { get; }
    public int Count
    {
        get { var count = 0; foreach (var word in _words) count += BitOperations.PopCount(word); return count; }
    }
    internal int WordCount => _words.Length;
    internal ulong GetWord(int index) => _words[index];
    public bool Contains(int runtimeIndex) => runtimeIndex >= 0 && runtimeIndex < Capacity &&
        (_words[runtimeIndex >> 6] & (1UL << (runtimeIndex & 63))) != 0;

    public IntentBitSet Intersect(IntentBitSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Capacity != other.Capacity) throw new ArgumentException("Bitsets must have the same capacity.", nameof(other));
        var words = new ulong[_words.Length];
        for (var index = 0; index < words.Length; index++) words[index] = _words[index] & other._words[index];
        return new IntentBitSet(Capacity, words);
    }

    public IEnumerable<int> EnumerateSetBits()
    {
        for (var wordIndex = 0; wordIndex < _words.Length; wordIndex++)
        {
            var word = _words[wordIndex];
            while (word != 0)
            {
                var bit = BitOperations.TrailingZeroCount(word);
                yield return (wordIndex << 6) + bit;
                word &= word - 1;
            }
        }
    }

    internal static IntentBitSet From(int capacity, IEnumerable<int> indexes)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        var words = new ulong[(capacity + 63) >> 6];
        foreach (var index in indexes)
        {
            if (index < 0 || index >= capacity) throw new ArgumentOutOfRangeException(nameof(indexes));
            words[index >> 6] |= 1UL << (index & 63);
        }
        return new IntentBitSet(capacity, words);
    }

    public static IntentBitSet FromIndexes(int capacity, IEnumerable<int> indexes)
    { ArgumentNullException.ThrowIfNull(indexes); return From(capacity, indexes); }
}

public readonly record struct IntentCandidateContext(bool HasJob, bool HasHome, bool HasWorkplace,
    bool HasSocialRelations, bool HasNetworkRelations = false);

// These sets are compiled once with the intent catalogue. "Available" sets are
// deliberately inclusive: intersecting them can only remove an intent when a
// prerequisite implied by its target/execution contract is definitely absent.
public sealed class IntentCandidateIndex
{
    private IntentCandidateIndex(IntentBitSet global, IntentBitSet withoutJob, IntentBitSet withoutHome,
        IntentBitSet withoutWorkplace, IntentBitSet withoutSocialRelations, IntentBitSet withoutNetworkRelations)
    {
        Global = global;
        AvailableWithoutJob = withoutJob;
        AvailableWithoutHome = withoutHome;
        AvailableWithoutWorkplace = withoutWorkplace;
        AvailableWithoutSocialRelations = withoutSocialRelations;
        AvailableWithoutNetworkRelations = withoutNetworkRelations;
    }

    public IntentBitSet Global { get; }
    public IntentBitSet AvailableWithoutJob { get; }
    public IntentBitSet AvailableWithoutHome { get; }
    public IntentBitSet AvailableWithoutWorkplace { get; }
    public IntentBitSet AvailableWithoutSocialRelations { get; }
    public IntentBitSet AvailableWithoutNetworkRelations { get; }

    internal static IntentCandidateIndex Build(IReadOnlyList<CompiledIntent> intents, int fallbackIndex)
    {
        var candidates = intents.Where(intent => intent.RuntimeIndex != fallbackIndex).ToArray();
        var global = IntentBitSet.From(intents.Count, candidates.Select(intent => (int)intent.RuntimeIndex));
        return new IntentCandidateIndex(global,
            IntentBitSet.From(intents.Count, Array.Empty<int>()),
            Available(candidates, intents.Count, intent => intent.Target is not { Kind: TargetKind.Location, Location: LocationValue.Home }),
            Available(candidates, intents.Count, intent => intent.Target is not { Kind: TargetKind.Location, Location: LocationValue.Work }),
            Available(candidates, intents.Count, intent => intent.Target.Query?.Relation != TargetRelationKind.Social),
            Available(candidates, intents.Count, intent => intent.Target.Query?.Relation is null or TargetRelationKind.Social));
    }

    private static IntentBitSet Available(IEnumerable<CompiledIntent> intents, int count,
        Func<CompiledIntent, bool> availableWithoutRequirement) => IntentBitSet.From(count,
        intents.Where(availableWithoutRequirement).Select(intent => (int)intent.RuntimeIndex));

    public IntentBitSet GetCandidates(IntentCandidateContext context)
    {
        var result = Global;
        if (!context.HasJob) result = result.Intersect(AvailableWithoutJob);
        if (!context.HasHome) result = result.Intersect(AvailableWithoutHome);
        if (!context.HasWorkplace) result = result.Intersect(AvailableWithoutWorkplace);
        if (!context.HasSocialRelations) result = result.Intersect(AvailableWithoutSocialRelations);
        if (!context.HasNetworkRelations) result = result.Intersect(AvailableWithoutNetworkRelations);
        return result;
    }

    // The hot path applies the same intersections a word at a time and visits
    // only set bits; it therefore neither allocates nor scans the intent array.
    internal CandidateEnumerable EnumerateCandidates(IntentCandidateContext context) => new(this, context);

    internal readonly struct CandidateEnumerable(IntentCandidateIndex index, IntentCandidateContext context)
    {
        public Enumerator GetEnumerator() => new(index, context);
    }

    internal struct Enumerator
    {
        private readonly IntentCandidateIndex _index;
        private readonly IntentCandidateContext _context;
        private int _wordIndex;
        private ulong _word;

        internal Enumerator(IntentCandidateIndex index, IntentCandidateContext context)
        { _index = index; _context = context; _wordIndex = -1; _word = 0; Current = 0; }

        public int Current { get; private set; }
        public bool MoveNext()
        {
            while (_word == 0)
            {
                if (++_wordIndex >= _index.Global.WordCount) return false;
                _word = _index.Global.GetWord(_wordIndex);
                if (!_context.HasJob) _word &= _index.AvailableWithoutJob.GetWord(_wordIndex);
                if (!_context.HasHome) _word &= _index.AvailableWithoutHome.GetWord(_wordIndex);
                if (!_context.HasWorkplace) _word &= _index.AvailableWithoutWorkplace.GetWord(_wordIndex);
                if (!_context.HasSocialRelations) _word &= _index.AvailableWithoutSocialRelations.GetWord(_wordIndex);
                if (!_context.HasNetworkRelations) _word &= _index.AvailableWithoutNetworkRelations.GetWord(_wordIndex);
            }
            var bit = BitOperations.TrailingZeroCount(_word);
            Current = (_wordIndex << 6) + bit;
            _word &= _word - 1;
            return true;
        }
    }
}
