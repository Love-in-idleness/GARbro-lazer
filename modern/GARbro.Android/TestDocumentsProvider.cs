#if GARBRO_ANDROID_TEST
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Android.Content;
using global::Android.Database;
using global::Android.OS;
using global::Android.Provider;
using Document = global::Android.Provider.DocumentsContract.Document;

namespace GARbro.Mobile;

/// <summary>Real content:// I/O in the internal APK, including revocation and provider limitations.</summary>
[ContentProvider(new[] { Authority }, Name = "org.garbro.garbro.TestDocumentsProvider", Exported = true, GrantUriPermissions = true,
    ReadPermission = "android.permission.MANAGE_DOCUMENTS", WritePermission = "android.permission.MANAGE_DOCUMENTS")]
public sealed class TestDocumentsProvider : DocumentsProvider
{
    public const string Authority = "org.garbro.garbro.test.documents";
    public static bool Available = true, RenameSupported = true;
    public static long? ReportedSize;
    string Root => Path.Combine(Context.FilesDir.AbsolutePath, "document-tests");
    static readonly string[] columns = { Document.ColumnDocumentId, Document.ColumnDisplayName, Document.ColumnMimeType, Document.ColumnSize, Document.ColumnLastModified, Document.ColumnFlags };
    public override bool OnCreate() { Directory.CreateDirectory(Root); return true; }
    string Physical(string id)
    {
        if (!Available) throw new Java.Lang.SecurityException("Test permission was revoked.");
        string path = Path.GetFullPath(Path.Combine(Root, id == "root" ? "" : id)); string relative = Path.GetRelativePath(Root, path);
        if (relative == ".." || relative.StartsWith("../") || Path.IsPathRooted(relative)) throw new Java.IO.FileNotFoundException(); return path;
    }
    string Id(string path) { string relative = Path.GetRelativePath(Root, path); return relative == "." ? "root" : relative; }
    void Add(MatrixCursor cursor, string path)
    {
        bool folder = Directory.Exists(path);
        var values = new Dictionary<string, object>
        {
            [Document.ColumnDocumentId] = Id(path), [Document.ColumnDisplayName] = Path.GetFileName(path),
            [Document.ColumnMimeType] = folder ? Document.MimeTypeDir : "application/octet-stream",
            [Document.ColumnSize] = folder ? 0L : ReportedSize ?? new FileInfo(path).Length,
            [Document.ColumnLastModified] = (long)(File.GetLastWriteTimeUtc(path) - DateTime.UnixEpoch).TotalMilliseconds,
            [Document.ColumnFlags] = (int)(folder ? DocumentContractFlags.DirSupportsCreate : DocumentContractFlags.SupportsWrite) |
                (int)DocumentContractFlags.SupportsDelete | (RenameSupported ? (int)DocumentContractFlags.SupportsRename : 0)
        };
        cursor.AddRow(cursor.GetColumnNames().Select(name => Box(values.GetValueOrDefault(name))).ToArray());
    }
    static Java.Lang.Object Box(object value) => value switch { string text => new Java.Lang.String(text), int number => new Java.Lang.Integer(number), long number => new Java.Lang.Long(number), _ => null };
    public override ICursor QueryRoots(string[] projection) => new MatrixCursor(projection ?? new[] { DocumentsContract.Root.ColumnRootId });
    public override ICursor QueryDocument(string documentId, string[] projection)
    { var cursor = new MatrixCursor(projection ?? columns); Add(cursor, Physical(documentId)); return cursor; }
    public override bool IsChildDocument(string parentDocumentId, string documentId)
    {
        string relative = Path.GetRelativePath(Physical(parentDocumentId), Physical(documentId));
        return relative != ".." && !relative.StartsWith("../") && !Path.IsPathRooted(relative);
    }
    public override ICursor QueryChildDocuments(string parentDocumentId, string[] projection, string sortOrder)
    {
        var cursor = new MatrixCursor(projection ?? columns);
        foreach (string path in Directory.EnumerateFileSystemEntries(Physical(parentDocumentId))) Add(cursor, path); return cursor;
    }
    public override ParcelFileDescriptor OpenDocument(string documentId, string mode, CancellationSignal signal) =>
        ParcelFileDescriptor.Open(new Java.IO.File(Physical(documentId)), ParcelFileDescriptor.ParseMode(mode));
    public override string CreateDocument(string parentDocumentId, string mimeType, string displayName)
    {
        if (displayName.IndexOfAny(new[] { '/', '\\' }) >= 0) throw new Java.IO.IOException("Invalid name.");
        string path = Path.Combine(Physical(parentDocumentId), displayName);
        if (File.Exists(path) || Directory.Exists(path)) throw new Java.IO.IOException("Output exists.");
        if (mimeType == Document.MimeTypeDir) Directory.CreateDirectory(path); else File.Create(path).Dispose(); return Id(path);
    }
    public override string RenameDocument(string documentId, string displayName)
    {
        if (!RenameSupported) throw new Java.Lang.UnsupportedOperationException();
        string path = Physical(documentId), target = Path.Combine(Path.GetDirectoryName(path), displayName);
        if (Directory.Exists(path)) Directory.Move(path, target); else File.Move(path, target); return Id(target);
    }
    public override void DeleteDocument(string documentId)
    { string path = Physical(documentId); if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }
}
#endif
