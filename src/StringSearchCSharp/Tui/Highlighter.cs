using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using StringSearch.Model;

namespace StringSearch.Tui;

public enum TokenClass : byte
{
    None,
    Keyword,
    ControlKeyword,
    Type,
    Method,
    String,
    Number,
    Comment,
    Preprocessor,
}

/// <summary>
/// Syntactic highlighting from the Roslyn syntax tree (no semantic model, no extra
/// dependency). Each file is classified once into one <see cref="TokenClass"/> per character.
/// </summary>
public static class Highlighter
{
    private static readonly ConditionalWeakTable<SourceDocument, TokenClass[]> Cache = new();

    private static readonly HashSet<SyntaxKind> StringKinds = Enum.GetValues<SyntaxKind>()
        .Where(k => k.ToString() is var n &&
            (n.Contains("StringLiteral") || n.Contains("InterpolatedString") || n.Contains("RawString") ||
             n.Contains("StringStart") || n.Contains("StringEnd") || n == nameof(SyntaxKind.CharacterLiteralToken)) &&
            n.EndsWith("Token"))
        .ToHashSet();

    private static readonly HashSet<SyntaxKind> ControlKeywords =
    [
        SyntaxKind.IfKeyword, SyntaxKind.ElseKeyword, SyntaxKind.SwitchKeyword, SyntaxKind.CaseKeyword,
        SyntaxKind.ForKeyword, SyntaxKind.ForEachKeyword, SyntaxKind.WhileKeyword, SyntaxKind.DoKeyword,
        SyntaxKind.BreakKeyword, SyntaxKind.ContinueKeyword, SyntaxKind.ReturnKeyword, SyntaxKind.GotoKeyword,
        SyntaxKind.TryKeyword, SyntaxKind.CatchKeyword, SyntaxKind.FinallyKeyword, SyntaxKind.ThrowKeyword,
        SyntaxKind.YieldKeyword, SyntaxKind.AwaitKeyword,
    ];

    /// <summary>Identifiers that act as keywords when used as a type (var x, dynamic d).</summary>
    private static readonly HashSet<string> TypeKeywordNames = ["var", "dynamic"];

    public static TokenClass[] For(SourceDocument document) =>
        Cache.GetValue(document, d => Classify(d.Text));

    public static Style StyleOf(TokenClass tokenClass) => tokenClass switch
    {
        TokenClass.Keyword => Theme.Keyword,
        TokenClass.ControlKeyword => Theme.ControlKeyword,
        TokenClass.Type => Theme.TypeName,
        TokenClass.Method => Theme.MethodName,
        TokenClass.String => Theme.StringLiteral,
        TokenClass.Number => Theme.NumberLiteral,
        TokenClass.Comment => Theme.Comment,
        TokenClass.Preprocessor => Theme.Preprocessor,
        _ => Style.Default,
    };

    public static TokenClass[] Classify(SourceText text)
    {
        var classes = new TokenClass[text.Length];
        var root = CSharpSyntaxTree.ParseText(text).GetRoot();

        foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
        {
            var tokenClass = ClassifyToken(token);
            if (tokenClass != TokenClass.None)
                Fill(classes, token.Span, tokenClass);
        }

        // Trivia last, so comments and directives win over the tokens structured inside them
        // (doc-comment XML, "#if DEBUG").
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsDirective)
            {
                Fill(classes, trivia.Span, TokenClass.Preprocessor);
                continue;
            }

            switch (trivia.Kind())
            {
                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.MultiLineCommentTrivia:
                case SyntaxKind.SingleLineDocumentationCommentTrivia:
                case SyntaxKind.MultiLineDocumentationCommentTrivia:
                case SyntaxKind.DocumentationCommentExteriorTrivia:
                case SyntaxKind.DisabledTextTrivia:
                    Fill(classes, trivia.Span, TokenClass.Comment);
                    break;
            }
        }

        return classes;
    }

    private static TokenClass ClassifyToken(SyntaxToken token)
    {
        var kind = token.Kind();

        if (StringKinds.Contains(kind))
            return TokenClass.String;
        if (kind == SyntaxKind.NumericLiteralToken)
            return TokenClass.Number;
        if (ControlKeywords.Contains(kind))
            return TokenClass.ControlKeyword;
        if (SyntaxFacts.IsKeywordKind(kind) || SyntaxFacts.IsContextualKeyword(kind))
            return TokenClass.Keyword;
        if (kind == SyntaxKind.IdentifierToken)
            return ClassifyIdentifier(token);
        return TokenClass.None;
    }

    private static TokenClass ClassifyIdentifier(SyntaxToken token)
    {
        switch (token.Parent)
        {
            case BaseTypeDeclarationSyntax:
            case DelegateDeclarationSyntax:
            case TypeParameterSyntax:
            case ConstructorDeclarationSyntax:
                return TokenClass.Type;
            case MethodDeclarationSyntax:
            case LocalFunctionStatementSyntax:
                return TokenClass.Method;
            case SimpleNameSyntax name:
                return ClassifyName(name, token);
            default:
                return TokenClass.None;
        }
    }

    private static TokenClass ClassifyName(SimpleNameSyntax name, SyntaxToken token)
    {
        // The name being called: M(), x.M(), M<T>().
        ExpressionSyntax callee = name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;
        if (callee.Parent is InvocationExpressionSyntax invocation && invocation.Expression == callee)
            return token.Text == "nameof" ? TokenClass.Keyword : TokenClass.Method;

        if (name is IdentifierNameSyntax && TypeKeywordNames.Contains(token.Text) && SyntaxFacts.IsInTypeOnlyContext(name))
            return TokenClass.Keyword;

        // The outermost name of a qualified type (System.Collections.List) is what counts for the context.
        ExpressionSyntax outer = name;
        while (outer.Parent is QualifiedNameSyntax qualified && qualified.Right == outer)
            outer = qualified;

        if (SyntaxFacts.IsInTypeOnlyContext(outer) || outer.Parent is ObjectCreationExpressionSyntax or TypeOfExpressionSyntax)
            return TokenClass.Type;

        // Attribute names ([Fact]) and generic names used as expressions (List<int>.Empty) read as types.
        if (outer.Parent is AttributeSyntax || name is GenericNameSyntax)
            return TokenClass.Type;

        return TokenClass.None;
    }

    private static void Fill(TokenClass[] classes, TextSpan span, TokenClass tokenClass)
    {
        int end = Math.Min(span.End, classes.Length);
        for (int i = span.Start; i < end; i++)
            classes[i] = tokenClass;
    }
}
