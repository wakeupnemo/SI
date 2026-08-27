using Lingware.Spard.Expressions;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SIQuester.ViewModel.Services;

/// <summary>Identifies the semantic role of one rendered SPARD editor token.</summary>
public enum SpardTokenKind
{
    Text,
    Alias,
    Line,
    OptionalStart,
    OptionalEnd,
    Opaque,
}

/// <summary>Describes one display token and its stable path into the parsed expression tree.</summary>
public sealed record SpardEditorToken(
    SpardTokenKind Kind,
    string Text,
    string Value,
    int Start,
    int Length,
    int Depth,
    string ExpressionPath,
    string SourceExpressionType)
{
    /// <summary>Gets the exclusive display end offset.</summary>
    public int End => Start + Length;
}

/// <summary>Defines a display-text selection.</summary>
public readonly record struct SpardSelection(int Start, int Length)
{
    /// <summary>Gets the exclusive selection end.</summary>
    public int End => Start + Length;
}

/// <summary>Provides committed SPARD text.</summary>
public sealed class SpardTextCommittedEventArgs(string text) : EventArgs
{
    /// <summary>Gets canonical text committed by the editor.</summary>
    public string Text { get; } = text;
}

/// <summary>
/// Owns parsed SPARD state, display tokens, structural selection, caret movement, and edit operations
/// without depending on a UI framework.
/// </summary>
public sealed class SpardEditorController : INotifyPropertyChanged
{
    private readonly Func<string, string> _aliasDisplayName;
    private readonly List<Node> _root = [];
    private readonly List<TokenBinding> _bindings = [];
    private readonly Dictionary<Node, NodeSpan> _nodeSpans = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<SpardEditorToken> _tokens = Array.Empty<SpardEditorToken>();
    private string _displayText = "";
    private string _serializedText = "";
    private string? _preservedMalformedText;
    private int _caretOffset;
    private SpardSelection _selection;
    private bool _hasParseError;
    private string? _parseError;

    /// <summary>Gets flattened display tokens mapped to expression paths.</summary>
    public IReadOnlyList<SpardEditorToken> Tokens => _tokens;

    /// <summary>Gets text presented by a renderer.</summary>
    public string DisplayText => _displayText;

    /// <summary>Gets canonical serialized SPARD, or the untouched malformed source before the first edit.</summary>
    public string SerializedText => _preservedMalformedText ?? _serializedText;

    /// <summary>Gets the current display caret offset.</summary>
    public int CaretOffset => _caretOffset;

    /// <summary>Gets the current structurally valid display selection.</summary>
    public SpardSelection Selection => _selection;

    /// <summary>Gets whether the source failed canonical parsing and is being preserved as literal text.</summary>
    public bool HasParseError => _hasParseError;

    /// <summary>Gets a safe parser diagnostic when <see cref="HasParseError"/> is true.</summary>
    public string? ParseError => _parseError;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when a UI host explicitly commits, normally on focus loss.</summary>
    public event EventHandler<SpardTextCommittedEventArgs>? Committed;

    /// <summary>Raised after a successful editor mutation.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Initializes an empty controller.</summary>
    public SpardEditorController(Func<string, string>? aliasDisplayName = null)
    {
        _aliasDisplayName = aliasDisplayName ?? (name => name);
        Rebuild();
    }

    /// <summary>Parses a template and rebuilds its structural token mapping.</summary>
    public void Load(string? text)
    {
        var source = text ?? "";
        _root.Clear();
        _preservedMalformedText = null;
        _hasParseError = false;
        _parseError = null;

        try
        {
            var expression = source.Length == 0 ? null : ExpressionBuilder.Parse(source, false);

            if (expression is Sequence sequence)
            {
                AddExpressions(_root, sequence.Operands());
            }
            else if (expression != null)
            {
                AddExpression(_root, expression);
            }
        }
        catch (Exception exception)
        {
            _hasParseError = true;
            _parseError = exception.Message;
            _preservedMalformedText = source;

            if (source.Length > 0)
            {
                _root.Add(new TextNode(source, null));
            }
        }

        _caretOffset = 0;
        _selection = default;
        Rebuild();
        OnPropertyChanged(nameof(HasParseError));
        OnPropertyChanged(nameof(ParseError));
    }

    /// <summary>Moves the caret, snapping away from the interior of structural tokens.</summary>
    public void SetCaret(int offset, bool preferForward = true)
    {
        var next = SnapCaret(Math.Clamp(offset, 0, _displayText.Length), preferForward);

        if (_caretOffset == next && _selection.Length == 0)
        {
            return;
        }

        _caretOffset = next;
        _selection = new SpardSelection(next, 0);
        NotifyCaretAndSelection();
    }

    /// <summary>
    /// Selects display text and expands partial aliases, line tokens, opaque nodes, and crossed optional
    /// boundaries into structurally valid ranges.
    /// </summary>
    public void SetSelection(int start, int length)
    {
        var normalizedStart = Math.Clamp(start, 0, _displayText.Length);
        var normalizedLength = Math.Max(0, length);
        var normalizedEnd = normalizedLength > _displayText.Length - normalizedStart
            ? _displayText.Length
            : normalizedStart + normalizedLength;
        var expanded = ExpandSelection(normalizedStart, normalizedEnd);
        _selection = new SpardSelection(expanded.Start, expanded.End - expanded.Start);
        _caretOffset = expanded.End;
        NotifyCaretAndSelection();
    }

    /// <summary>Inserts literal text at the caret, replacing the current structural selection.</summary>
    public void InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return;
        }

        DeleteSelectionCore();
        var insertionOffset = _caretOffset;
        var textBinding = _bindings.FirstOrDefault(binding =>
            binding.Token.Kind == SpardTokenKind.Text
            && insertionOffset >= binding.Token.Start
            && insertionOffset <= binding.Token.End);

        if (textBinding?.Node is TextNode textNode)
        {
            var relative = insertionOffset - textBinding.Token.Start;
            textNode.Value = string.Concat(
                textNode.Value.AsSpan(0, relative),
                text,
                textNode.Value.AsSpan(relative));
            textNode.Modified = true;
        }
        else
        {
            var insertion = ResolveInsertion(insertionOffset);
            insertion.List.Insert(insertion.Index, new TextNode(text, null));
        }

        CompleteEdit(insertionOffset + text.Length);
    }

    /// <summary>Inserts an alias token using the canonical match instruction.</summary>
    public void InsertAlias(string aliasName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aliasName);
        InsertNode(new AliasNode(aliasName, aliasName == "Some", null), placeCaretInsideOptional: false);
    }

    /// <summary>Inserts a canonical line token.</summary>
    public void InsertLine() => InsertNode(new LineNode(null), placeCaretInsideOptional: false);

    /// <summary>Inserts an empty optional or wraps the current structurally valid selection.</summary>
    public void InsertOptional()
    {
        if (_selection.Length == 0)
        {
            InsertNode(new OptionalNode(null), placeCaretInsideOptional: true);
            return;
        }

        var selection = _selection;
        var list = FindDeepestSelectionList(_root, selection.Start, selection.End);
        SplitTextAt(list, selection.Start);
        Rebuild();
        SplitTextAt(list, selection.End);
        Rebuild();

        var selected = list
            .Where(node => _nodeSpans.TryGetValue(node, out var span)
                && span.Start >= selection.Start
                && span.End <= selection.End)
            .ToArray();

        if (selected.Length == 0)
        {
            return;
        }

        var index = list.IndexOf(selected[0]);

        foreach (var node in selected)
        {
            list.Remove(node);
        }

        var optional = new OptionalNode(null);
        optional.Children.AddRange(selected);
        list.Insert(index, optional);
        CompleteEdit(selection.Start, optional, placeCaretInsideOptional: false);
    }

    /// <summary>Deletes one literal character or one complete structural token before the caret.</summary>
    public bool DeleteBackward()
    {
        if (_selection.Length == 0)
        {
            if (_caretOffset == 0)
            {
                return false;
            }

            SetSelection(_caretOffset - 1, 1);
        }

        return DeleteSelection();
    }

    /// <summary>Deletes one literal character or one complete structural token after the caret.</summary>
    public bool DeleteForward()
    {
        if (_selection.Length == 0)
        {
            if (_caretOffset >= _displayText.Length)
            {
                return false;
            }

            SetSelection(_caretOffset, 1);
        }

        return DeleteSelection();
    }

    /// <summary>Moves one literal character or one atomic structural token backward.</summary>
    public bool MoveCaretBackward()
    {
        if (_selection.Length > 0)
        {
            SetCaret(_selection.Start, preferForward: false);
            return true;
        }

        if (_caretOffset == 0)
        {
            return false;
        }

        SetCaret(_caretOffset - 1, preferForward: false);
        return true;
    }

    /// <summary>Moves one literal character or one atomic structural token forward.</summary>
    public bool MoveCaretForward()
    {
        if (_selection.Length > 0)
        {
            SetCaret(_selection.End);
            return true;
        }

        if (_caretOffset >= _displayText.Length)
        {
            return false;
        }

        SetCaret(_caretOffset + 1);
        return true;
    }

    /// <summary>Publishes canonical editor text to the owning view model.</summary>
    public void Commit() => Committed?.Invoke(this, new SpardTextCommittedEventArgs(SerializedText));

    private bool DeleteSelection()
    {
        if (_selection.Length == 0)
        {
            return false;
        }

        var caret = _selection.Start;
        DeleteSelectionCore();
        CompleteEdit(caret);
        return true;
    }

    private void DeleteSelectionCore()
    {
        if (_selection.Length == 0)
        {
            return;
        }

        DeleteRange(_root, _selection.Start, _selection.End);
        _caretOffset = _selection.Start;
        _selection = new SpardSelection(_caretOffset, 0);
        NormalizeNodes(_root);
        Rebuild();
    }

    private void InsertNode(Node node, bool placeCaretInsideOptional)
    {
        DeleteSelectionCore();
        var insertion = ResolveInsertion(_caretOffset);
        insertion.List.Insert(insertion.Index, node);
        CompleteEdit(_caretOffset, node, placeCaretInsideOptional);
    }

    private void CompleteEdit(int requestedCaret, Node? insertedNode = null, bool placeCaretInsideOptional = false)
    {
        _preservedMalformedText = null;
        _hasParseError = false;
        _parseError = null;
        NormalizeNodes(_root);
        Rebuild();

        if (insertedNode != null && _nodeSpans.TryGetValue(insertedNode, out var span))
        {
            requestedCaret = placeCaretInsideOptional && insertedNode is OptionalNode
                ? span.OpeningEnd
                : span.End;
        }

        _caretOffset = SnapCaret(Math.Clamp(requestedCaret, 0, _displayText.Length), preferForward: true);
        _selection = new SpardSelection(_caretOffset, 0);
        NotifyCaretAndSelection();
        OnPropertyChanged(nameof(HasParseError));
        OnPropertyChanged(nameof(ParseError));
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private InsertionPoint ResolveInsertion(int offset)
    {
        foreach (var binding in _bindings)
        {
            if (binding.Optional != null)
            {
                if (binding.Token.Kind == SpardTokenKind.OptionalStart && offset == binding.Token.End)
                {
                    return new InsertionPoint(binding.Optional.Children, 0);
                }

                if (binding.Token.Kind == SpardTokenKind.OptionalEnd && offset == binding.Token.Start)
                {
                    return new InsertionPoint(binding.Optional.Children, binding.Optional.Children.Count);
                }
            }

            if (offset <= binding.Token.Start)
            {
                return new InsertionPoint(binding.Parent, binding.Index);
            }

            if (offset < binding.Token.End && binding.Node is TextNode textNode)
            {
                var relative = offset - binding.Token.Start;
                var right = new TextNode(textNode.Value[relative..], null) { Modified = true };
                textNode.Value = textNode.Value[..relative];
                textNode.Modified = true;
                binding.Parent.Insert(binding.Index + 1, right);
                return new InsertionPoint(binding.Parent, binding.Index + 1);
            }

            if (offset <= binding.Token.End)
            {
                return new InsertionPoint(binding.Parent, binding.Index + 1);
            }
        }

        return new InsertionPoint(_root, _root.Count);
    }

    private List<Node> FindDeepestSelectionList(List<Node> list, int start, int end)
    {
        foreach (var node in list.OfType<OptionalNode>())
        {
            if (_nodeSpans.TryGetValue(node, out var span)
                && start >= span.OpeningEnd
                && end <= span.ClosingStart)
            {
                return FindDeepestSelectionList(node.Children, start, end);
            }
        }

        return list;
    }

    private void SplitTextAt(List<Node> list, int offset)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (list[index] is not TextNode textNode
                || !_nodeSpans.TryGetValue(textNode, out var span)
                || offset <= span.Start
                || offset >= span.End)
            {
                continue;
            }

            var relative = offset - span.Start;
            var left = new TextNode(textNode.Value[..relative], null) { Modified = true };
            var right = new TextNode(textNode.Value[relative..], null) { Modified = true };
            list[index] = left;
            list.Insert(index + 1, right);
            return;
        }
    }

    private void DeleteRange(List<Node> list, int start, int end)
    {
        for (var index = list.Count - 1; index >= 0; index--)
        {
            var node = list[index];

            if (!_nodeSpans.TryGetValue(node, out var span) || end <= span.Start || start >= span.End)
            {
                continue;
            }

            if (node is TextNode textNode)
            {
                var localStart = Math.Clamp(start - span.Start, 0, textNode.Value.Length);
                var localEnd = Math.Clamp(end - span.Start, localStart, textNode.Value.Length);
                textNode.Value = string.Concat(textNode.Value.AsSpan(0, localStart), textNode.Value.AsSpan(localEnd));
                textNode.Modified = true;

                if (textNode.Value.Length == 0)
                {
                    list.RemoveAt(index);
                }
            }
            else if (node is OptionalNode optional
                && start >= span.OpeningEnd
                && end <= span.ClosingStart)
            {
                DeleteRange(optional.Children, start, end);
            }
            else
            {
                list.RemoveAt(index);
            }
        }
    }

    private SelectionRange ExpandSelection(int start, int end)
    {
        if (start == end)
        {
            return new SelectionRange(start, end);
        }

        var expandedStart = start;
        var expandedEnd = end;
        var changed = true;

        while (changed)
        {
            changed = false;

            foreach (var binding in _bindings.Where(binding => binding.Token.Kind != SpardTokenKind.Text))
            {
                if (expandedEnd > binding.Token.Start && expandedStart < binding.Token.End)
                {
                    var nextStart = Math.Min(expandedStart, binding.Token.Start);
                    var nextEnd = Math.Max(expandedEnd, binding.Token.End);
                    changed |= nextStart != expandedStart || nextEnd != expandedEnd;
                    expandedStart = nextStart;
                    expandedEnd = nextEnd;
                }
            }

            foreach (var optional in _nodeSpans.Where(pair => pair.Key is OptionalNode).Select(pair => pair.Value))
            {
                var enters = expandedStart <= optional.Start
                    && expandedEnd > optional.Start
                    && expandedEnd < optional.End;
                var exits = expandedStart > optional.Start
                    && expandedStart < optional.End
                    && expandedEnd >= optional.End;

                if (!enters && !exits)
                {
                    continue;
                }

                var nextStart = Math.Min(expandedStart, optional.Start);
                var nextEnd = Math.Max(expandedEnd, optional.End);
                changed |= nextStart != expandedStart || nextEnd != expandedEnd;
                expandedStart = nextStart;
                expandedEnd = nextEnd;
            }
        }

        return new SelectionRange(expandedStart, expandedEnd);
    }

    private int SnapCaret(int offset, bool preferForward)
    {
        foreach (var binding in _bindings)
        {
            if (binding.Token.Kind != SpardTokenKind.Text
                && offset > binding.Token.Start
                && offset < binding.Token.End)
            {
                return preferForward ? binding.Token.End : binding.Token.Start;
            }
        }

        return offset;
    }

    private void AddExpressions(List<Node> target, IEnumerable<Expression> expressions)
    {
        foreach (var expression in expressions)
        {
            AddExpression(target, expression);
        }
    }

    private void AddExpression(List<Node> target, Expression expression)
    {
        if (expression is Sequence sequence)
        {
            AddExpressions(target, sequence.Operands());
            return;
        }

        target.Add(CreateNode(expression));
    }

    private Node CreateNode(Expression expression)
    {
        if (expression is Optional optional)
        {
            var node = new OptionalNode(optional);

            if (optional.Operand is Sequence sequence)
            {
                AddExpressions(node.Children, sequence.Operands());
            }
            else if (optional.Operand != null)
            {
                AddExpression(node.Children, optional.Operand);
            }

            return node;
        }

        if (expression is Instruction instruction)
        {
            if (instruction.Argument is StringValue stringValue)
            {
                return new TextNode(stringValue.Value, expression);
            }

            if (instruction.Argument is Set set && TryGetSetName(set, out var aliasName))
            {
                return new AliasNode(aliasName, false, expression);
            }
        }

        if (expression is Set directSet && TryGetSetName(directSet, out var setName))
        {
            return setName == "Line"
                ? new LineNode(expression)
                : new AliasNode(setName, true, expression);
        }

        if (expression is StringValue bareString)
        {
            return new TextNode(bareString.Value, expression);
        }

        return new OpaqueNode(expression);
    }

    private static bool TryGetSetName(Set set, out string name)
    {
        var value = set.Operand?.Operands().FirstOrDefault() as StringValue;

        if (value != null)
        {
            name = value.Value;
            return true;
        }

        name = "";
        return false;
    }

    private void Rebuild()
    {
        _bindings.Clear();
        _nodeSpans.Clear();
        var publicTokens = new List<SpardEditorToken>();
        var display = new System.Text.StringBuilder();
        BuildTokens(_root, publicTokens, display, 0, "");
        _tokens = publicTokens;
        _displayText = display.ToString();
        _serializedText = new Sequence(_root.Select(node => node.ToExpression()).ToArray()).ToString();
        OnPropertyChanged(nameof(Tokens));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(SerializedText));
    }

    private void BuildTokens(
        List<Node> list,
        List<SpardEditorToken> publicTokens,
        System.Text.StringBuilder display,
        int depth,
        string parentPath)
    {
        for (var index = 0; index < list.Count; index++)
        {
            var node = list[index];
            var path = parentPath.Length == 0 ? index.ToString() : $"{parentPath}/{index}";
            var start = display.Length;

            if (node is OptionalNode optional)
            {
                AddToken(node, list, index, optional, SpardTokenKind.OptionalStart, "(", "", depth, path + "/start", publicTokens, display);
                var openingEnd = display.Length;
                BuildTokens(optional.Children, publicTokens, display, depth + 1, path);
                var closingStart = display.Length;
                AddToken(node, list, index, optional, SpardTokenKind.OptionalEnd, ")?", "", depth, path + "/end", publicTokens, display);
                _nodeSpans[node] = new NodeSpan(start, display.Length, openingEnd, closingStart);
                continue;
            }

            var tokenData = node switch
            {
                TextNode text => (SpardTokenKind.Text, text.Value, text.Value),
                AliasNode alias => (SpardTokenKind.Alias, _aliasDisplayName(alias.Name), alias.Name),
                LineNode => (SpardTokenKind.Line, "\n", "Line"),
                OpaqueNode opaque => (
                    SpardTokenKind.Opaque,
                    opaque.Expression.ToString() ?? "",
                    opaque.Expression.ToString() ?? ""),
                _ => throw new InvalidOperationException($"Unsupported SPARD node {node.GetType().Name}"),
            };
            AddToken(node, list, index, null, tokenData.Item1, tokenData.Item2, tokenData.Item3, depth, path, publicTokens, display);
            _nodeSpans[node] = new NodeSpan(start, display.Length, start, display.Length);
        }
    }

    private void AddToken(
        Node node,
        List<Node> parent,
        int index,
        OptionalNode? optional,
        SpardTokenKind kind,
        string text,
        string value,
        int depth,
        string path,
        List<SpardEditorToken> publicTokens,
        System.Text.StringBuilder display)
    {
        var token = new SpardEditorToken(
            kind,
            text,
            value,
            display.Length,
            text.Length,
            depth,
            path,
            node.Original?.GetType().Name ?? node.GetType().Name);
        publicTokens.Add(token);
        _bindings.Add(new TokenBinding(token, node, parent, index, optional));
        display.Append(text);
    }

    private static void NormalizeNodes(List<Node> list)
    {
        for (var index = list.Count - 1; index >= 0; index--)
        {
            if (list[index] is TextNode emptyText && emptyText.Value.Length == 0)
            {
                list.RemoveAt(index);
                continue;
            }

            if (list[index] is OptionalNode optional)
            {
                NormalizeNodes(optional.Children);
            }
        }

        for (var index = 0; index + 1 < list.Count;)
        {
            if (list[index] is TextNode left && list[index + 1] is TextNode right)
            {
                left.Value += right.Value;
                left.Modified = true;
                list.RemoveAt(index + 1);
                continue;
            }

            index++;
        }
    }

    private void NotifyCaretAndSelection()
    {
        OnPropertyChanged(nameof(CaretOffset));
        OnPropertyChanged(nameof(Selection));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private abstract class Node(Expression? original)
    {
        internal Expression? Original { get; } = original;
        internal abstract Expression ToExpression();
    }

    private sealed class TextNode(string value, Expression? original) : Node(original)
    {
        internal string Value { get; set; } = value;
        internal bool Modified { get; set; }

        internal override Expression ToExpression()
        {
            if (!Modified && Original != null)
            {
                return Original;
            }

            if (Original is Instruction instruction && instruction.Argument is StringValue stringValue)
            {
                stringValue.Value = Value;
                return instruction;
            }

            return new Instruction(new StringValue("ignoresp"), new StringValue(Value));
        }
    }

    private sealed class AliasNode(string name, bool direct, Expression? original) : Node(original)
    {
        internal string Name { get; } = name;

        internal override Expression ToExpression() => Original
            ?? (direct
                ? new Set(Name)
                : new Instruction(new StringValue("m"), new Set(Name)));
    }

    private sealed class LineNode(Expression? original) : Node(original)
    {
        internal override Expression ToExpression() => Original ?? new Set("Line");
    }

    private sealed class OptionalNode(Optional? original) : Node(original)
    {
        internal List<Node> Children { get; } = [];

        internal override Expression ToExpression() =>
            new Optional(new Sequence(Children.Select(node => node.ToExpression()).ToArray()));
    }

    private sealed class OpaqueNode(Expression expression) : Node(expression)
    {
        internal Expression Expression { get; } = expression;
        internal override Expression ToExpression() => Expression;
    }

    private sealed record TokenBinding(
        SpardEditorToken Token,
        Node Node,
        List<Node> Parent,
        int Index,
        OptionalNode? Optional);

    private readonly record struct InsertionPoint(List<Node> List, int Index);
    private readonly record struct SelectionRange(int Start, int End);
    private readonly record struct NodeSpan(int Start, int End, int OpeningEnd, int ClosingStart);
}
