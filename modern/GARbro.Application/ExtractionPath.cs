using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GARbro.Application
{
    public static class ExtractionPath
    {
        public static string Resolve(string root, string entry)
        {
            if (string.IsNullOrWhiteSpace(entry) || entry[0] == '/' || entry[0] == '\\' || Path.IsPathRooted(entry))
                throw new InvalidDataException("Archive entry has an absolute or empty path: " + entry);
            var parts = entry.Normalize(NormalizationForm.FormC).Split(new[] { '/', '\\' });
            foreach (var part in parts)
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(' ') || part.EndsWith('.') || part.Any(c => c < 32 || ":*?\"<>|".Contains(c)) || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("Unsafe archive entry path: " + entry);
            root = Path.GetFullPath(root);
            var path = Path.GetFullPath(Path.Combine(new[] { root }.Concat(parts).ToArray()));
            var relative = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                throw new InvalidDataException("Entry leaves the output directory: " + entry);
            RejectLinks(root, path);
            return path;
        }

        public static void RejectLinks(string root, string path)
        {
            root = Path.GetFullPath(root);
            path = Path.GetFullPath(path);
            for (string p = path; p != null; p = Path.GetDirectoryName(p))
            {
                try
                {
                    if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Extraction through a symbolic link is not allowed: " + p);
                }
                catch (FileNotFoundException)
                {
                }
                catch (DirectoryNotFoundException)
                {
                }

                if (string.Equals(p, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    break;
            }
        }
    }
}
