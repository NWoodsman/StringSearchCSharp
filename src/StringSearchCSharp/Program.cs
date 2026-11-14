using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;


if(args.Length != 1)
{
    Console.WriteLine("No arg provided for the target path");
    return;
}

string targetDirectory = args[0];


if (!Directory.Exists(targetDirectory))
{
    Console.WriteLine("Directory not found.");
    return;
}

var csFiles = Directory.EnumerateFiles(targetDirectory, "*.cs", SearchOption.AllDirectories);

foreach (var file in csFiles)
{
    // Skip auto-generated files in bin/obj folders
    if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
    {
        continue;
    }

    string code = File.ReadAllText(file);
    SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
    SyntaxNode root = tree.GetRoot();

    // Find standard string literals ("hello") and raw/verbatim strings (@"hello", """hello""")
    var stringLiterals = root.DescendantNodes()
        .OfType<LiteralExpressionSyntax>()
        .Where(node => node.IsKind(SyntaxKind.StringLiteralExpression) ||
                       node.IsKind(SyntaxKind.Utf8StringLiteralExpression));

    // Find interpolated string text parts ($"Hello {name}")
    var interpolatedTexts = root.DescendantNodes()
        .OfType<InterpolatedStringTextSyntax>();

    foreach (var literal in stringLiterals)
    {
        var line = literal.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        Console.WriteLine($"[{Path.GetFileName(file)}:{line}] {literal.Token.ValueText}");
    }

    foreach (var text in interpolatedTexts)
    {
        var line = text.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        Console.WriteLine($"[{Path.GetFileName(file)}:{line}] (Interpolated) {text.TextToken.ValueText}");
    }
}
