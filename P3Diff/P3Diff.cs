using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DiffMatchPatch;

namespace P3Diff
{
    /* P3Diff: Create a P3Patch archive by comparing the source and destination
     * 
     * Usage:  P3Diff.exe <sourcePath> <destinationPath>
     * For example:  P3Diff.exe c:\P3\P3_SDK_V0.8\P3SampleApp C:\P3\P3EmptyGame
     * The output archive is saved at the destinationPath with the extension .p3patch appended.
     * 
     * The format of ZipArchive is:
     *      manifest
     *      same/<destinationRelativePath>
     *      new/<destinationRelativePath>
     *      diff/<destinationRelativePath>
     * 
     * When applying the patch with P3Patch.exe,
     * - Same files are copied identically. The ZipArchiveEntry content is the sourceRelativePath.
     * - New files are created from the ZipArchiveEntry content.
     * - Files with differences are patched according to the instructions in the ZipArchiveEntry
     *     The format of diff ZipArchiveEntry is:
     *        sourceRelativePath
     *        =NNN     (meaning copy NNN characters from the source)
     *        -NNN     (meaning skip NNN characters from the source effectively deleting them)
     *        +quotedText   (meaning copy the given unquoted text effectively inserting it)
     *        
     *        To quote text, replace: \ by \\, newline by \ and n, carriage return by \ and r
     */

    class P3Diff
    {
        private string srcRoot;
        private string destRoot;
        private string srcAppCode;
        private string destAppCode;
        private ZipArchive archive;

        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            if (args.Length != 2)
            {
                throw new Exception("Usage: P3Diff <sourcePath> <destinationPath>");
            }
            P3Diff p3diff = new P3Diff(args[0], args[1]);
            p3diff.CreatePackage();
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is OutOfMemoryException)
            {
                Exception exception = (Exception)e.ExceptionObject;
                Console.WriteLine("OutOfMemoryException occurred:");
                Console.WriteLine(exception.StackTrace);
            }
        }

        public P3Diff(string srcRoot, string destRoot)
        {
            this.srcRoot = srcRoot;
            this.destRoot = destRoot;
        }

        private void CreatePackage()
        {
            Console.WriteLine("Creating P3Patch with source " + srcRoot + " and destination " + destRoot);

            srcAppCode = FindAppCode(srcRoot + "\\Configuration\\AppConfig.json");
            destAppCode = FindAppCode(destRoot + "\\Configuration\\AppConfig.json");
            Console.WriteLine("Source AppCode=" + srcAppCode);
            Console.WriteLine("Destination AppCode=" + destAppCode);

            string zipPath = destRoot + ".p3patch";
            Console.WriteLine("Create P3Patch archive " + zipPath);

            using (FileStream outputStream = new FileStream(zipPath, FileMode.Create))
            using (archive = new ZipArchive(outputStream, ZipArchiveMode.Create, true))
            {
                string manifest = "version:1\n";
                CreateZipEntry("manifest", manifest);
                DirSearch(destRoot);
            }
        }

        private void DirSearch(string dir)
        {
            foreach (string f in Directory.GetFiles(dir))
            {
                if (!IsSkipped(f))
                {
                    ProcessFile(f);
                }
            }

            foreach (string d in Directory.GetDirectories(dir))
            {
                if (!IsSkipped(d))
                {
                    DirSearch(d);
                }
            }
        }

        private void ProcessFile(string destPath)
        {
            //Console.WriteLine("ProcessFile(" + destPath + ")");

            string relPath = destPath.Substring(destRoot.Length + 1);
            int sep = relPath.LastIndexOf('\\');
            string relDir = (sep == -1) ? "" : relPath.Substring(0, sep + 1);
            string destFilename = (sep == -1) ? relPath : relPath.Substring(sep + 1);
            string srcFileName = destFilename.StartsWith(destAppCode) ? (srcAppCode + destFilename.Substring(destAppCode.Length)) : destFilename;
            string srcRelPath = relDir + srcFileName;
            string srcPath = srcRoot + '\\' + srcRelPath;

            if (File.Exists(srcPath))
            {
                List<Diff> diffs = DiffFiles(srcPath, destPath);
                if (diffs.Count == 1 && diffs[0].operation == Operation.EQUAL)
                {
                    Console.WriteLine("same " + destPath);
                    CreateZipEntry("same\\" + relPath, srcRelPath);
                }
                else
                {
                    Console.WriteLine("diff " + destPath);
                    StringBuilder sb = new StringBuilder();
                    sb.Append(srcRelPath).Append("\n");
                    SerializeDiffs(diffs, sb);
                    string diffPatch = sb.ToString();
                    // Console.WriteLine(diffPatch);
                    CreateZipEntry("diff\\" + relPath, diffPatch);
                }
            }
            else
            {
                Console.WriteLine("new " + destPath);
                byte[] content = System.IO.File.ReadAllBytes(destPath);
                CreateZipEntry("new\\" + relPath, content);
            }
        }

        private List<Diff> DiffFiles(string srcPath, string destPath)
        {
            string srcText = ReadFile(srcPath);
            string destText = ReadFile(destPath);

            diff_match_patch dmp = new diff_match_patch();
            dmp.Diff_Timeout = 0;
            List<Diff> diffs = dmp.diff_main(srcText, destText);

            if (diffs.Count > 1 && IsText(srcPath))
            {
                string destText2 = destText.Replace("\r", "");
                List<Diff> diffs2 = dmp.diff_main(srcText, destText2);
                if (diffs2.Count < diffs.Count)
                {
                    diffs = diffs2;
                }
            }

            return diffs;
        }

        private void SerializeDiffs(List<Diff> diffs, StringBuilder sb)
        {
            for (int i = 0; i < diffs.Count; i++)
            {
                Diff diff = diffs[i];
                switch (diff.operation)
                {
                    case Operation.INSERT:
                        sb.Append('+').AppendLine(QuoteText(diff.text));
                        break;

                    case Operation.EQUAL:
                        sb.Append('=').AppendLine(diff.text.Length.ToString());
                        break;

                    case Operation.DELETE:
                        sb.Append('-').AppendLine(diff.text.Length.ToString());
                        break;
                }
            }
        }

        private string ReadFile(string path)
        {
            // Use ISO-8859-1 encoding to work with text or binary files
            string content;
            using (StreamReader reader = new StreamReader(path, Encoding.GetEncoding("ISO8859-1")))
            {
                content = reader.ReadToEnd();
            }
            return content;
        }

        private string FindAppCode(string path)
        {
            string contents = ReadFile(path);
            Match match = Regex.Match(contents, "\"Name\": \"(\\w+)\"");
            if (!match.Success)
            {
                throw new Exception("Cannot find AppCode in " + path);
            }

            string appcode = match.Groups[1].Value;
            return appcode;
        }

        private string QuoteText(string text)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in text)
            {

                if (ch == '\\')
                {
                    sb.Append(@"\\");
                }
                else if (ch == '\n')
                {
                    sb.Append(@"\n");
                }
                else if (ch == '\r')
                {
                    sb.Append(@"\r");
                }
                else
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        public void CreateZipEntry(string entryName, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            CreateZipEntry(entryName, bytes);
        }

        public void CreateZipEntry(string entryName, byte[] content)
        {
            var entry = archive.CreateEntry(entryName);
            using (var entryStream = entry.Open())
            {
                entryStream.Write(content, 0, content.Length);
            }
        }

        private string[] skipped = { "\\.git", "\\Documentation", "\\Library", "\\obj", "\\Temp", "\\.vs", ".sln", ".csproj" };
        private bool IsSkipped(string f)
        {
            for (int i=0; i<skipped.Length; i++)
            {
                if (f.EndsWith(skipped[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private string[] bin_extensions = { "ogg", ".wav", ".sfk", ".mp3", ".png", ".zip", ".ttf", ".tif", ".so", ".dylib", ".dll", ".tga", ".jpg", ".psd" };
        private bool IsBinary(string path)
        {
            for (int i = 0; i < bin_extensions.Length; i++)
            {
                if (path.EndsWith(bin_extensions[i]))
                {
                    return true;
                }
            }

            byte[] content = System.IO.File.ReadAllBytes(path);
            foreach (byte b in content)
            {
                if (b < 32 && b != '\t' && b != '\n' && b != '\r')
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsText(string path)
        {
            if (path.EndsWith(".cs") || path.EndsWith(".meta"))
            {
                return true;
            }

            return !IsBinary(path);
        }
    }
}
