using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DiffMatchPatch;

namespace P3Patch
{
    /* P3Patch: Apply a P3Patch to the source to recreate the destination
     * 
     * Usage:  P3Patch.exe <sourcePath> <p3patchPath>
     * 
     * For example:  P3Patch.exe c:\P3\P3_SDK_V0.8\P3SampleApp c:\P3\P3EmptyGame.p3patch
     * The output is under the directory based on the p3patchPath without the extension.
     * In the example, the output is under c:\P3\P3EmptyGame
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
     *      The format of a diff ZipArchiveEntry is:
     *        sourceRelativePath
     *        =NNN     (meaning copy NNN characters from the source)
     *        -NNN     (meaning skip NNN characters from the source effectively deleting them)
     *        +quotedText   (meaning copy the given unquoted text effectively inserting it)
     *        
     *        To quote text, replace: \ by \\, newline by \ and n, carriage return by \ and r
     */

    class P3Patch
    {
        private string srcRoot;
        private string destRoot;
        private string patchPath;

        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            if (args.Length != 2)
            {
                throw new Exception("Usage: P3Patch  <sourcePath> <p3patchPath>");
            }
            P3Patch p3patch = new P3Patch(args[0], args[1]);
            p3patch.ApplyPatch();
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

        public P3Patch(string srcRoot, string patchPath)
        {
            this.srcRoot = srcRoot;
            this.patchPath = patchPath;

            if (!patchPath.EndsWith(".p3patch"))
            {
                throw new Exception("Expecting a patch archive ending with .p3patch: " + patchPath);
            }
            destRoot = patchPath.Substring(0, patchPath.Length - 8);
        }

        private void ApplyPatch()
        {
            Console.WriteLine("Applying P3Patch with source " + srcRoot + " and patch archive " + patchPath);

            if (File.Exists(destRoot))
            {
                throw new Exception("A file already exists at " + destRoot);
            }

            if (Directory.Exists(destRoot))
            {
                throw new Exception("Output directory already exists: " + destRoot);
            }

            Directory.CreateDirectory(destRoot);

            using (ZipArchive archive = new ZipArchive(File.OpenRead(patchPath), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    Console.WriteLine("Entry: " + entry.FullName);

                    if (entry.FullName.Equals("manifest"))
                    {
                        // ignored, all patches are version: 1
                    }
                    else if (entry.FullName.StartsWith(@"same\"))
                    {
                        ProcessSameFile(entry);
                    }
                    else if (entry.FullName.StartsWith(@"new\"))
                    {
                        ProcessNewFile(entry);
                    }
                    else if (entry.FullName.StartsWith(@"diff\"))
                    {
                        ProcessDiffFile(entry);
                    }
                    else
                    {
                        Console.WriteLine("Unexpected Zip entry: " + entry.FullName);
                    }
                }
            }
        }

        private void ProcessSameFile(ZipArchiveEntry entry)
        {
            string srcRelPath = ReadZipEntryToEnd(entry);
            string srcPath = srcRoot + "\\" + srcRelPath;
            string destRelPath = entry.FullName.Substring(5);
            string destPath = destRoot + "\\" + destRelPath;
            MakeParentDirectory(destPath);
            File.Copy(srcPath, destPath);
        }

        private void ProcessNewFile(ZipArchiveEntry entry)
        {
            string destRelPath = entry.FullName.Substring(4);
            string destPath = destRoot + "\\" + destRelPath;
            MakeParentDirectory(destPath);
            CopyZipEntryToFile(entry, destPath);
        }

        private void ProcessDiffFile(ZipArchiveEntry entry)
        {
            string destRelPath = entry.FullName.Substring(5);
            string destPath = destRoot + "\\" + destRelPath;
            MakeParentDirectory(destPath);

            string alldiffs = ReadZipEntryToEnd(entry);

            using (StreamWriter writer = new StreamWriter(destPath, false, Encoding.GetEncoding("ISO-8859-1")))
            using (Stream entryStream = entry.Open())
            using (StreamReader diffReader = new StreamReader(entryStream))
            {
                string srcRelPath = diffReader.ReadLine();
                string srcPath = srcRoot + "\\" + srcRelPath;
                //Console.WriteLine("diff srcPath is " + srcPath);
                using (StreamReader srcReader = new StreamReader(srcPath, Encoding.GetEncoding("ISO-8859-1")))
                {
                    string line;
                    while ((line = diffReader.ReadLine()) != null)
                    {
                        switch (line[0])
                        {
                            case '=':
                                int numCopy = int.Parse(line.Substring(1));
                                for (int i=0; i<numCopy; i++)
                                {
                                    int ch = srcReader.Read();
                                    if (ch == -1)
                                    {
                                        throw new Exception("Unexpected EOF reading " + srcPath);
                                    }
                                    writer.Write((char)ch);
                                }
                                break;

                            case '-':
                                int numSkip = int.Parse(line.Substring(1));
                                for (int i = 0; i < numSkip; i++)
                                {
                                    srcReader.Read(); // discard byte
                                }
                                break;

                            case '+':
                                string insertText = UnquoteText(line, 1);
                                writer.Write(insertText);
                                break;

                            default:
                                throw new Exception("Unexpected diff operation: " + line[0]);
                        }
                    }
                }
            }
        }

        private void MakeParentDirectory(string path)
        {
            int sep = path.LastIndexOf('\\');
            string dir = path.Substring(0, sep);
            Directory.CreateDirectory(dir);
        }

        private string ReadZipEntryToEnd(ZipArchiveEntry entry)
        {
            using (StreamReader reader = new StreamReader(entry.Open()))
            {
                return reader.ReadToEnd();
            }
        }

        private void CopyZipEntryToFile(ZipArchiveEntry entry, string path)
        {
            using (Stream entryStream = entry.Open())
            using (FileStream output = File.Create(path))
            {
                entryStream.CopyTo(output);
            }
        }

        private string UnquoteText(string text, int offset)
        {
            StringBuilder sb = new StringBuilder();
            bool escaping = false;
            for (int i=offset; i<text.Length; i++)
            {
                char ch = text[i];
                if (escaping)
                {
                    escaping = false;
                    if (ch == '\\')
                    {
                        sb.Append('\\');
                    }
                    else if (ch == 'n')
                    {
                        sb.Append('\n');
                    }
                    else if (ch == 'r')
                    {
                        sb.Append('\r');
                    }
                    else
                    {
                        throw new Exception("Unexpected escape sequence: \\" + ch);
                    }
                }
                else if (ch == '\\')
                {
                    escaping = true;
                }
                else
                {
                    sb.Append(ch);
                }
            }

            if (escaping)
            {
                throw new Exception("Invalid escape at EOF");
            }

            return sb.ToString();
        }
    }
}
