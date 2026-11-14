namespace StringSearch.Model;

/// <summary>
/// Identifies a string within a file. Both parts are compared ordinally, so values that differ
/// by a single character are different keys. Record-struct equality hashes both fields together.
/// </summary>
public readonly record struct SearchKey(string RelativePath, string Value);
