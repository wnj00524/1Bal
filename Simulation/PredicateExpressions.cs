namespace ProxyState.Simulation;

// Authoring predicates deliberately distinguish boolean facts from numeric
// comparisons so malformed type combinations fail while content is loaded.
public sealed record PredicateDefinition
{
    public string? Op { get; init; }
    public string? Fact { get; init; }
    public bool? Value { get; init; }
    public PredicateDefinition? Input { get; init; }
    public List<PredicateDefinition>? Inputs { get; init; }
    public NumericExpressionDefinition? Left { get; init; }
    public NumericExpressionDefinition? Right { get; init; }
}

internal enum PredicateOpcode : byte { BooleanFact, Constant, And, Or, Not, Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }

internal readonly record struct PredicateInstruction(
    PredicateOpcode Opcode, FactId Fact = default,
    CompiledNumericExpression? Left = null, CompiledNumericExpression? Right = null);

public sealed class CompiledPredicate
{
    public const int MaximumDepth = 16;
    public const int MaximumInstructions = 64;
    private readonly PredicateInstruction[] _instructions;
    private readonly int _stackSize;

    private CompiledPredicate(PredicateInstruction[] instructions, int stackSize)
    {
        _instructions = instructions;
        _stackSize = stackSize;
    }

    public static CompiledPredicate Compile(PredicateDefinition? predicate, FactRegistry facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (predicate is null) throw new InvalidDataException("An eligibility predicate is required.");
        var instructions = new List<PredicateInstruction>();
        var stackSize = CompileNode(predicate, facts, instructions, 1);
        if (instructions.Count > MaximumInstructions)
            throw new InvalidDataException($"Predicate exceeds maximum complexity {MaximumInstructions}.");
        return new CompiledPredicate(instructions.ToArray(), stackSize);
    }

    internal bool Evaluate(in DecisionFactContext context)
    {
        Span<bool> stack = stackalloc bool[_stackSize];
        var top = 0;
        foreach (var instruction in _instructions)
        {
            switch (instruction.Opcode)
            {
                case PredicateOpcode.BooleanFact: stack[top++] = context.ReadBoolean(instruction.Fact); break;
                case PredicateOpcode.Constant: stack[top++] = instruction.Fact.Index != 0; break;
                case PredicateOpcode.Not: stack[top - 1] = !stack[top - 1]; break;
                case PredicateOpcode.And:
                case PredicateOpcode.Or:
                    var rightBoolean = stack[--top];
                    stack[top - 1] = instruction.Opcode == PredicateOpcode.And
                        ? stack[top - 1] && rightBoolean : stack[top - 1] || rightBoolean;
                    break;
                default:
                    var left = instruction.Left!.Evaluate(context);
                    var right = instruction.Right!.Evaluate(context);
                    stack[top++] = instruction.Opcode switch
                    {
                        PredicateOpcode.Equal => left == right,
                        PredicateOpcode.NotEqual => left != right,
                        PredicateOpcode.Less => left < right,
                        PredicateOpcode.LessOrEqual => left <= right,
                        PredicateOpcode.Greater => left > right,
                        PredicateOpcode.GreaterOrEqual => left >= right,
                        _ => throw new InvalidOperationException("Unsupported compiled predicate opcode.")
                    };
                    break;
            }
        }
        return stack[0];
    }

    private static int CompileNode(PredicateDefinition node, FactRegistry facts,
        List<PredicateInstruction> output, int depth)
    {
        if (depth > MaximumDepth) throw new InvalidDataException($"Predicate exceeds maximum depth {MaximumDepth}.");
        if (output.Count >= MaximumInstructions) throw new InvalidDataException($"Predicate exceeds maximum complexity {MaximumInstructions}.");
        var op = node.Op?.ToLowerInvariant() ?? throw new InvalidDataException("Predicate op is required.");
        switch (op)
        {
            case "fact":
                var fact = facts.ResolveDefinition(node.Fact ?? string.Empty);
                if (fact.ValueKind != FactValueKind.Boolean)
                    throw new InvalidDataException($"Fact '{node.Fact}' is numeric and cannot be used as a boolean predicate.");
                output.Add(new PredicateInstruction(PredicateOpcode.BooleanFact, fact.Id));
                return 1;
            case "constant":
                if (!node.Value.HasValue) throw new InvalidDataException("Boolean predicate constant requires a value.");
                output.Add(new PredicateInstruction(PredicateOpcode.Constant, new FactId(default, node.Value.Value ? 1 : 0)));
                return 1;
            case "not":
                if (node.Input is null) throw new InvalidDataException("not requires an input predicate.");
                var unaryStack = CompileNode(node.Input, facts, output, depth + 1);
                output.Add(new PredicateInstruction(PredicateOpcode.Not));
                return unaryStack;
            case "and": case "or":
                if (node.Inputs is null || node.Inputs.Count < 2)
                    throw new InvalidDataException($"{op} requires at least two input predicates.");
                var stackSize = CompileNode(node.Inputs[0], facts, output, depth + 1);
                for (var index = 1; index < node.Inputs.Count; index++)
                {
                    var rightStack = CompileNode(node.Inputs[index], facts, output, depth + 1);
                    stackSize = Math.Max(stackSize, 1 + rightStack);
                    output.Add(new PredicateInstruction(op == "and" ? PredicateOpcode.And : PredicateOpcode.Or));
                }
                return stackSize;
            default:
                var opcode = op switch
                {
                    "equal" => PredicateOpcode.Equal, "notequal" => PredicateOpcode.NotEqual,
                    "less" => PredicateOpcode.Less, "lessorequal" => PredicateOpcode.LessOrEqual,
                    "greater" => PredicateOpcode.Greater, "greaterorequal" => PredicateOpcode.GreaterOrEqual,
                    _ => throw new InvalidDataException($"Unknown predicate op '{node.Op}'.")
                };
                if (node.Left is null || node.Right is null)
                    throw new InvalidDataException($"{op} requires left and right numeric expressions.");
                output.Add(new PredicateInstruction(opcode, Left: CompiledNumericExpression.Compile(node.Left, facts),
                    Right: CompiledNumericExpression.Compile(node.Right, facts)));
                return 1;
        }
    }
}
