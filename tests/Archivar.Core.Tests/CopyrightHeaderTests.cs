/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Tests;

/// <summary>
/// Every source file of our own carries the 71 Degrees copyright header (the same one TriviaNight uses): C#, JS, TypeScript and CSS
/// as a <c>/* ... */</c> block, Razor as a <c>@* ... *@</c> block, at the very top. Third-party code vendored under
/// <c>wwwroot/lib/</c> keeps its own license header and is skipped, as are build output folders.
/// </summary>
public class CopyrightHeaderTests
{
    private const string Notice = "Copyright © 2026 71 Degrees. All rights reserved.";

    private static readonly string[] Extensions = [".cs", ".razor", ".css", ".js", ".ts"];
    private static readonly string[] SkippedFolders = ["bin", "obj", "lib", "node_modules", ".git", ".vs", "publish_out"];

    [Fact]
    public void EveryOwnSourceFile_StartsWithTheCopyrightHeader()
    {
        var root = RepoRoot();
        var missing = new[] { "src", "tests", "tools" }
            .SelectMany(folder => SourceFiles(Path.Combine(root, folder)))
            .Where(file => !StartsWithHeader(file))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.True(missing.Count == 0,
            "These files are missing the 71 Degrees copyright header at the top (copy it from any other file of the same type):\n"
            + string.Join("\n", missing));
    }

    private static bool StartsWithHeader(string file)
    {
        var head = string.Join('\n', File.ReadLines(file).Take(5)).TrimStart('﻿');
        var opener = file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ? "@*" : "/*";
        return head.StartsWith(opener, StringComparison.Ordinal) && head.Contains(Notice, StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles(string folder)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(folder))
        {
            if (SkippedFolders.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in SourceFiles(sub))
            {
                yield return file;
            }
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Archivar.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repo root (Archivar.slnx) above " + AppContext.BaseDirectory);
    }
}
