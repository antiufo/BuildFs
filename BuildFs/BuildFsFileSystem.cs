using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using DokanNet;
using DokanNet.Logging;
using static DokanNet.FormatProviders;
using FileAccess = DokanNet.FileAccess;
using System.Text.RegularExpressions;
using Shaman.Runtime;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;

namespace BuildFs
{
    internal class BuildFsFileSystem : IDokanOperations
    {
        private readonly Dictionary<string, Project> projects = new Dictionary<string, Project>();

        private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData |
                                              FileAccess.Execute |
                                              FileAccess.GenericExecute | FileAccess.GenericWrite |
                                              FileAccess.GenericRead;

        public void RunCached(string proj, string workingDir, string command, params object[] args)
        {
            if (!Path.IsPathRooted(workingDir)) workingDir = Path.Combine("R:\\", proj, workingDir);
            var arr2 = new List<string>();
            arr2.Add("Run");
            arr2.Add(workingDir);
            arr2.Add(command);
            arr2.AddRange(args.Select(x => x.ToString()));

            var key = string.Join("\n", arr2);
            RunCached(proj, key, () =>
            {
                Console.WriteLine("Running from " + workingDir + ": " + ProcessUtils.GetCommandLine(command, args));
                ProcessUtils.RunPassThroughFrom(workingDir, command, args);
            });
        }


        public void RunCached(string proj, string key, Action action)
        {
            string keyHash;
            using (var sha1 = new SHA1Cng())
            {
                var arr = Encoding.UTF8.GetBytes(proj + "\n" + key);
                var hash = sha1.ComputeHash(arr);
                keyHash = Convert.ToBase64String(hash)
                    .Replace("/", "-")
                    .Replace("+", "_")
                    .Replace("=", string.Empty);
            }

            lock (_runLock)
            {
                using (var info = JsonFile.Open<ExecutionSummary>(Path.Combine("C:\\BuildFs\\Cache", keyHash + ".pb")))
                {
                    if (info.Content.Available && IsStatusStillValid(proj, info.Content))
                    {
                        Console.WriteLine("No need to run " + keyHash);
                        info.DiscardAll();
                    }
                    else
                    {
                        ClearStatus(proj);
                        action();
                        var changes = CaptureChanges(proj);
                        info.Content.Inputs = changes.Inputs;
                        info.Content.Outputs = changes.Outputs;
                        info.Content.Available = true;
                    }
                }
            }
        }
        private object _runLock = new object();

        internal bool IsStatusStillValid(string v, ExecutionSummary changes)
        {
            lock (_lock)
            {
                var proj = GetProjectByNameHasLock(v);
                if (changes.Inputs != null)
                {
                    foreach (var old in changes.Inputs)
                    {
                        var current = CaptureItemStatus(GetPathAware(old.Path)) ?? new ItemStatus();
                        if (current.Attributes == (FileAttributes)0 && old.Attributes != (FileAttributes)0)
                        {
                            Console.WriteLine("Changed, no longer exists: " + old.Path);
                            return false;
                        }
                        if (current.Attributes != (FileAttributes)0 && old.Attributes == (FileAttributes)0)
                        {
                            Console.WriteLine("Changed, new file exists: " + old.Path);
                            return false;
                        }
                        if (current.Attributes != old.Attributes)
                        {
                            Console.WriteLine("Changed attributes: " + old.Path);
                            Console.WriteLine(old.Attributes + " -> " + current.Attributes);
                            return false;
                        }
                        if (current.LastWriteTime != old.LastWriteTime)
                        {
                            Console.WriteLine("Changed last write time: " + old.Path);
                            Console.WriteLine(old.LastWriteTime + " -> " + current.LastWriteTime);
                            return false;
                        }
                        if (current.Size != old.Size)
                        {
                            Console.WriteLine("Changed size: " + old.Path);
                            Console.WriteLine(old.Size + " -> " + current.Size);
                            return false;
                        }

                    }
                }
            }
            return true;
        }

        internal ExecutionSummary CaptureChanges(string v)
        {
            lock (_lock)
            {
                var outputs = new List<string>();
                var inputs = new List<ItemStatus>();
                var proj = GetProjectByNameHasLock(v);
                foreach (var item in proj.Items)
                {
                    var pair = item.Value;
                    if (pair.Changed)
                    {
                        var physicalPath = GetPathAware(item.Key);
                        pair.After = CaptureItemStatus(physicalPath);
                        if (pair.Before != null && pair.Before.ContentsHash != null && (pair.After.Attributes & FileAttributes.Directory) == 0)
                        {
                            pair.After.ContentsHash = CaptureHash(item.Key);
                            if (ByteArraysEqual(pair.Before.ContentsHash, pair.After.ContentsHash))
                            {
                                pair.Changed = false;
                                if (pair.Before.LastWriteTime != pair.After.LastWriteTime)
                                {
                                    File.SetLastWriteTimeUtc(physicalPath, pair.Before.LastWriteTime);
                                }
                            }
                        }
                    }

                    if (pair.Changed)
                    {
                        outputs.Add(item.Key);
                    }
                    else 
                    {
                        var m = Path.GetFileName(item.Key);
                        if (m != "makefile" && m != "makefile-common")
                        {
                            var k = pair.Before;
                            if (k == null) k = new ItemStatus();
                            k.Path = item.Key;
                            inputs.Add(k);
                        }
                    }
                }
                return new ExecutionSummary() { Inputs = inputs, Outputs = outputs };
            }
        }

        private bool ByteArraysEqual(byte[] contentsHash1, byte[] contentsHash2)
        {
            if (contentsHash1.Length != contentsHash2.Length) return false;
            for (int i = 0; i < contentsHash1.Length; i++)
            {
                if (contentsHash1[i] != contentsHash2[i]) return false;
            }
            return true;
        }

        internal void ClearStatus(string projectName)
        {
            lock (_lock)
            {
                var k = GetProjectByNameHasLock(projectName);
                k.Items.Clear();
                k.readFiles.Clear();
                k.changedFiles.Clear();
            }
        }

        private const FileAccess DataWriteAccess = FileAccess.WriteData | FileAccess.AppendData |
                                                   FileAccess.Delete |
                                                   FileAccess.GenericWrite;

        private ConsoleLogger logger = new ConsoleLogger("[Mirror] ");

        public BuildFsFileSystem()
        {
        }


        private string GetPath(string fileName)
        {
            var p = GetPathAware(fileName);
            if (p == null)
            {
                return "C:\\NON_EXISTING_PATH";
            }
            return p;
        }
        private string GetPathAware(string fileName)
        {
            lock (_lock)
            {
                var proj = GetProjectFromPathHasLock(fileName);
                if (proj == null) return null;
                return proj.Path + fileName.Substring(1 + proj.Name.Length).Replace('/', '\\');
            }
        }

        private Project GetProjectFromPathHasLock(string fileName)
        {
            if (fileName == "\\") return null;
            var idx = fileName.IndexOf('\\', 1);
            if (idx == -1) idx = fileName.Length;
            var name = fileName.SubstringCached(1, idx - 1);
            Project p;
            projects.TryGetValue(name, out p);
            return p;
        }

        private Project GetProjectByNameHasLock(string projectName)
        {
            return projects.First(x => x.Key == projectName).Value;
        }
        internal void AddProject(string path, string name)
        {
            var proj = new Project()
            {
                Path = path,
                Name = name,
                changedFiles = new HashSet<string>(),
                Items = new Dictionary<string, BuildFs.ItemPair>(),
                readFiles = new HashSet<string>()
            };
            lock (_lock)
            {
                projects[name] = proj;
            }
        }

        private object _lock = new object();
        private char Letter;

        private NtStatus Trace(string method, string fileName, DokanFileInfo info, NtStatus result,
            params object[] parameters)
        {
#if TRACE
            var extraParameters = parameters != null && parameters.Length > 0
                ? ", " + string.Join(", ", parameters.Select(x => string.Format(DefaultFormatProvider, "{0}", x)))
                : string.Empty;

            logger.Debug(DokanFormat($"{method}('{fileName}', {info}{extraParameters}) -> {result}"));
#endif

            return result;
        }

        private NtStatus Trace(string method, string fileName, DokanFileInfo info,
            FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
            NtStatus result)
        {
#if TRACE
            logger.Debug(
                DokanFormat(
                    $"{method}('{fileName}', {info}, [{access}], [{share}], [{mode}], [{options}], [{attributes}]) -> {result}"));
#endif

            return result;
        }

        #region Implementation of IDokanOperations

        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode,
            FileOptions options, FileAttributes attributes, DokanFileInfo info)
        {

            var modifies =
                FileAccess.AccessSystemSecurity |
                FileAccess.AppendData |
                FileAccess.ChangePermissions |
                FileAccess.Delete |
                FileAccess.DeleteChild |
                FileAccess.GenericAll |
                FileAccess.GenericWrite |
                FileAccess.MaximumAllowed |
                FileAccess.SetOwnership |
                FileAccess.WriteAttributes |
                FileAccess.WriteData |
                FileAccess.WriteExtendedAttributes;

            if (
                fileName.IndexOf('*') != -1 ||
                fileName.IndexOf('?') != -1 ||
                fileName.IndexOf('>') != -1 ||
                fileName.IndexOf('<') != -1
                ) return NtStatus.ObjectNameInvalid;
                
            OnFileRead(fileName);
            if ((access & modifies) != 0)
            {
                OnFileChanged(fileName);
            }

            if (fileName == "\\")
            {
                if (mode != FileMode.Open) return DokanResult.AlreadyExists;
                info.IsDirectory = true;
                info.Context = new object();
                return NtStatus.Success;
            }

            NtStatus result = NtStatus.Success;
            var filePath = GetPathAware(fileName);

            if (filePath == null) return DokanResult.PathNotFound;

            if (info.IsDirectory)
            {
                try
                {
                    switch (mode)
                    {
                        case FileMode.Open:
                            if (!Directory.Exists(filePath))
                            {
                                try
                                {
                                    if (!File.GetAttributes(filePath).HasFlag(FileAttributes.Directory))
                                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                            attributes, NtStatus.NotADirectory);
                                }
                                catch (Exception)
                                {
                                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                        attributes, DokanResult.FileNotFound);
                                }
                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    attributes, DokanResult.PathNotFound);
                            }

                            //new DirectoryInfo(filePath).EnumerateFileSystemInfos().Any();
                            // you can't list the directory
                            break;

                        case FileMode.CreateNew:
                            if (Directory.Exists(filePath))
                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    attributes, DokanResult.FileExists);

                            try
                            {
                                File.GetAttributes(filePath).HasFlag(FileAttributes.Directory);
                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    attributes, DokanResult.AlreadyExists);
                            }
                            catch (IOException)
                            {
                            }

                            OnFileChanged(fileName);
                            Directory.CreateDirectory(GetPath(fileName));
                            break;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                        DokanResult.AccessDenied);
                }
            }
            else
            {
                var pathExists = true;
                var pathIsDirectory = false;

                var readWriteAttributes = (access & DataAccess) == 0;
                var readAccess = (access & DataWriteAccess) == 0;

                var attr = GetFileAttributes(filePath);
                pathExists = attr != FileAttributes_NotFound;
                pathIsDirectory = (attr & (uint)FileAttributes.Directory) != 0;

                switch (mode)
                {
                    case FileMode.Open:

                        if (pathExists)
                        {
                            if (readWriteAttributes || pathIsDirectory)
                            // check if driver only wants to read attributes, security info, or open directory
                            {
                                if (pathIsDirectory && (access & FileAccess.Delete) == FileAccess.Delete
                                    && (access & FileAccess.Synchronize) != FileAccess.Synchronize)
                                    //It is a DeleteFile request on a directory
                                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                        attributes, DokanResult.AccessDenied);

                                info.IsDirectory = pathIsDirectory;
                                info.Context = new object();
                                // must set it to someting if you return DokanError.Success

                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    attributes, DokanResult.Success);
                            }
                        }
                        else
                        {
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                                DokanResult.FileNotFound);
                        }
                        break;

                    case FileMode.CreateNew:
                        if (pathExists)
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                                DokanResult.FileExists);
                        break;

                    case FileMode.Truncate:
                        if (!pathExists)
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                                DokanResult.FileNotFound);
                        break;
                }

                try
                {
                    if (readAccess) share |= FileShare.Read;
                    info.Context = new FileStream(filePath, mode,
                        readAccess ? System.IO.FileAccess.Read : System.IO.FileAccess.ReadWrite, share, 4096, options);

                    if (pathExists && (mode == FileMode.OpenOrCreate
                        || mode == FileMode.Create))
                        result = DokanResult.AlreadyExists;

                    if (mode == FileMode.CreateNew || mode == FileMode.Create) //Files are always created as Archive
                        attributes |= FileAttributes.Archive;
                    File.SetAttributes(filePath, attributes);
                }
                catch (UnauthorizedAccessException) // don't have access rights
                {
                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                        DokanResult.AccessDenied);
                }
                catch (DirectoryNotFoundException)
                {
                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                        DokanResult.PathNotFound);
                }
                catch (Exception ex)
                {
                    var hr = (uint)Marshal.GetHRForException(ex);
                    switch (hr)
                    {
                        case 0x80070020: //Sharing violation
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                                DokanResult.SharingViolation);
                        default:
                            throw;
                    }
                }
            }
            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                result);
        }

        static bool IsDirectory(uint attrs)
        {
            return (attrs & (uint)FileAttributes.Directory) != 0;
        }

        public void Cleanup(string fileName, DokanFileInfo info)
        {
#if TRACE
            if (info.Context != null)
                Console.WriteLine(DokanFormat($"{nameof(Cleanup)}('{fileName}', {info} - entering"));
#endif

            (info.Context as FileStream)?.Dispose();
            info.Context = null;

            if (info.DeleteOnClose)
            {
                OnFileChanged(fileName);
                if (info.IsDirectory)
                {
                    Directory.Delete(GetPath(fileName));
                }
                else
                {
                    File.Delete(GetPath(fileName));
                }
            }
            Trace(nameof(Cleanup), fileName, info, DokanResult.Success);
        }

        public void CloseFile(string fileName, DokanFileInfo info)
        {
#if TRACE
            if (info.Context != null)
                Console.WriteLine(DokanFormat($"{nameof(CloseFile)}('{fileName}', {info} - entering"));
#endif

            (info.Context as FileStream)?.Dispose();
            info.Context = null;
            Trace(nameof(CloseFile), fileName, info, DokanResult.Success);
            // could recreate cleanup code here but this is not called sometimes
        }

        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, DokanFileInfo info)
        {
            OnFileRead(fileName);
            if (info.Context == null) // memory mapped read
            {
                using (var stream = new FileStream(GetPath(fileName), FileMode.Open, System.IO.FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    stream.Position = offset;
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
            }
            else // normal read
            {
                var stream = info.Context as FileStream;
                lock (stream) //Protect from overlapped read
                {
                    stream.Position = offset;
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
            }
            return Trace(nameof(ReadFile), fileName, info, DokanResult.Success, "out " + bytesRead.ToString(),
                offset.ToString(CultureInfo.InvariantCulture));
        }

        private void OnFileChanged(string fileName)
        {
            lock (_lock)
            {
                var proj = GetProjectFromPathHasLock(fileName);
                if (proj != null && proj.changedFiles.Add(fileName))
                {
                    Console.WriteLine("Changed: " + fileName);
                    var pair = GetItemPairHasLock(fileName);
                    if (pair != null)
                    {
                        pair.Changed = true;
                        var before = pair.Before;
                        if (before != null)
                        {
                            if ((before.Attributes & FileAttributes.Directory) == 0)
                            {
                                before.ContentsHash = CaptureHash(fileName);
                            }
                        }
                    }
                }
            }
        }

        private byte[] CaptureHash(string fileName)
        {
            using (var stream = File.Open(GetPathAware(fileName), FileMode.Open, System.IO.FileAccess.Read, FileShare.Delete | FileShare.Read))
            using (var sha1 = new SHA1Cng())
            {
                return sha1.ComputeHash(stream);
            }
        }

        private void OnFileRead(string fileName)
        {
            lock (_lock)
            {
                var proj = GetProjectFromPathHasLock(fileName);
                if (proj != null && proj.readFiles.Add(fileName))
                {
                    Console.WriteLine("Read: " + fileName);
                    var pair = GetItemPairHasLock(fileName);
                }
            }
        }
        private void SaveRepositoryStatus()
        {
            lock (_lock)
            {
            }
        }
        private ItemPair GetItemPairHasLock(string fileName)
        {
            var proj = GetProjectFromPathHasLock(fileName);
            if (proj == null) return null;
            var k = proj.Items;
            ItemPair m;
            if (k.TryGetValue(fileName, out m)) return m;

            var z = GetPathAware(fileName);
            if (z == null) return null;

            k[fileName] = new ItemPair() { Before = CaptureItemStatus(z) };
            return m;
        }

        private ItemStatus CaptureItemStatus(string physicalPath)
        {
            if (File.Exists(physicalPath))
            {
                var fi = new FileInfo(physicalPath);
                return new ItemStatus()
                {
                    Attributes = (fi.Attributes | FileAttributes.Archive) & ~FileAttributes.Normal,
                    LastWriteTime = fi.LastWriteTimeUtc,
                    Size = fi.Length
                };
            }
            else if (Directory.Exists(physicalPath))
            {
                var di = new DirectoryInfo(physicalPath);
                return new ItemStatus() { Attributes = di.Attributes & ~FileAttributes.Normal };
            }
            return null;
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            if (info.Context == null)
            {
                using (var stream = new FileStream(GetPath(fileName), FileMode.Open, System.IO.FileAccess.Write, FileShare.Delete))
                {
                    stream.Position = offset;
                    stream.Write(buffer, 0, buffer.Length);
                    bytesWritten = buffer.Length;
                }
            }
            else
            {
                var stream = info.Context as FileStream;
                lock (stream) //Protect from overlapped write
                {
                    stream.Position = offset;
                    stream.Write(buffer, 0, buffer.Length);
                }
                bytesWritten = buffer.Length;
            }
            return Trace(nameof(WriteFile), fileName, info, DokanResult.Success, "out " + bytesWritten.ToString(),
                offset.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).Flush();
                return Trace(nameof(FlushFileBuffers), fileName, info, DokanResult.Success);
            }
            catch (IOException)
            {
                return Trace(nameof(FlushFileBuffers), fileName, info, DokanResult.DiskFull);
            }
        }

        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, DokanFileInfo info)
        {
            OnFileRead(fileName);
            if (fileName == "\\")
            {
                fileInfo = new FileInformation()
                {
                    Attributes = FileAttributes.Directory,
                    FileName = fileName
                };
                return NtStatus.Success;
            }
            // may be called with info.Context == null, but usually it isn't
            var filePath = GetPath(fileName);
            var attr = GetFileAttributes(filePath);
            if (attr == FileAttributes_NotFound)
            {
                fileInfo = default(FileInformation);
                return DokanResult.FileNotFound;
            }
            var finfo = IsDirectory(attr) ? new DirectoryInfo(filePath) : (FileSystemInfo)new FileInfo(filePath);

            fileInfo = new FileInformation
            {
                FileName = fileName,
                Attributes = finfo.Attributes,
                CreationTime = finfo.CreationTime,
                LastAccessTime = finfo.LastAccessTime,
                LastWriteTime = finfo.LastWriteTime,
                Length = (finfo as FileInfo)?.Length ?? 0,
            };
            return Trace(nameof(GetFileInformation), fileName, info, DokanResult.Success);
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, DokanFileInfo info)
        {
            // This function is not called because FindFilesWithPattern is implemented
            // Return DokanResult.NotImplemented in FindFilesWithPattern to make FindFiles called
            files = FindFilesHelper(fileName, "*");

            return Trace(nameof(FindFiles), fileName, info, DokanResult.Success);
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            try
            {
                File.SetAttributes(GetPath(fileName), attributes);
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.Success, attributes.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.AccessDenied, attributes.ToString());
            }
            catch (FileNotFoundException)
            {
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.FileNotFound, attributes.ToString());
            }
            catch (DirectoryNotFoundException)
            {
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.PathNotFound, attributes.ToString());
            }
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
            DateTime? lastWriteTime, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            try
            {
                var filePath = GetPath(fileName);
                if (creationTime.HasValue)
                    File.SetCreationTime(filePath, creationTime.Value);

                if (lastAccessTime.HasValue)
                    File.SetLastAccessTime(filePath, lastAccessTime.Value);

                if (lastWriteTime.HasValue)
                    File.SetLastWriteTime(filePath, lastWriteTime.Value);

                return Trace(nameof(SetFileTime), fileName, info, DokanResult.Success, creationTime, lastAccessTime,
                    lastWriteTime);
            }
            catch (UnauthorizedAccessException)
            {
                return Trace(nameof(SetFileTime), fileName, info, DokanResult.AccessDenied, creationTime, lastAccessTime,
                    lastWriteTime);
            }
            catch (FileNotFoundException)
            {
                return Trace(nameof(SetFileTime), fileName, info, DokanResult.FileNotFound, creationTime, lastAccessTime,
                    lastWriteTime);
            }
        }

        public NtStatus DeleteFile(string fileName, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            var filePath = GetPath(fileName);

            if (Directory.Exists(filePath))
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.AccessDenied);

            if (!File.Exists(filePath))
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.FileNotFound);

            if (File.GetAttributes(filePath).HasFlag(FileAttributes.Directory))
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.AccessDenied);

            return Trace(nameof(DeleteFile), fileName, info, DokanResult.Success);
            // we just check here if we could delete the file - the true deletion is in Cleanup
        }

        public NtStatus DeleteDirectory(string fileName, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            return Trace(nameof(DeleteDirectory), fileName, info,
                Directory.EnumerateFileSystemEntries(GetPath(fileName)).Any()
                    ? DokanResult.DirectoryNotEmpty
                    : DokanResult.Success);
            // if dir is not empty it can't be deleted
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            OnFileRead(oldName);
            OnFileChanged(oldName);
            OnFileChanged(newName);
            var oldpath = GetPath(oldName);
            var newpath = GetPathAware(newName);
            if (newpath == null) return DokanResult.PathNotFound;

            (info.Context as FileStream)?.Dispose();
            info.Context = null;

            var exist = info.IsDirectory ? Directory.Exists(newpath) : File.Exists(newpath);

            try
            {

                if (!exist)
                {
                    info.Context = null;
                    if (info.IsDirectory)
                        Directory.Move(oldpath, newpath);
                    else
                        File.Move(oldpath, newpath);
                    return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName,
                        replace.ToString(CultureInfo.InvariantCulture));
                }
                else if (replace)
                {
                    info.Context = null;

                    if (info.IsDirectory) //Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
                        return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName,
                            replace.ToString(CultureInfo.InvariantCulture));

                    File.Delete(newpath);
                    File.Move(oldpath, newpath);
                    return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName,
                        replace.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (UnauthorizedAccessException)
            {
                return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName,
                    replace.ToString(CultureInfo.InvariantCulture));
            }
            return Trace(nameof(MoveFile), oldName, info, DokanResult.FileExists, newName,
                replace.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            try
            {
                ((FileStream)(info.Context)).SetLength(length);
                return Trace(nameof(SetEndOfFile), fileName, info, DokanResult.Success,
                    length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace(nameof(SetEndOfFile), fileName, info, DokanResult.DiskFull,
                    length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            try
            {
                ((FileStream)(info.Context)).SetLength(length);
                return Trace(nameof(SetAllocationSize), fileName, info, DokanResult.Success,
                    length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace(nameof(SetAllocationSize), fileName, info, DokanResult.DiskFull,
                    length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).Lock(offset, length);
                return Trace(nameof(LockFile), fileName, info, DokanResult.Success,
                    offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace(nameof(LockFile), fileName, info, DokanResult.AccessDenied,
                    offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).Unlock(offset, length);
                return Trace(nameof(UnlockFile), fileName, info, DokanResult.Success,
                    offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace(nameof(UnlockFile), fileName, info, DokanResult.AccessDenied,
                    offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus GetDiskFreeSpace(out long free, out long total, out long used, DokanFileInfo info)
        {
            free = 0;
            total = 0;
            used = 0;
            return NtStatus.NotImplemented;
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
            out string fileSystemName, DokanFileInfo info)
        {
            volumeLabel = "DOKAN";
            fileSystemName = "NTFS";

            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
                       FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage |
                       FileSystemFeatures.UnicodeOnDisk;

            return Trace(nameof(GetVolumeInformation), null, info, DokanResult.Success, "out " + volumeLabel,
                "out " + features.ToString(), "out " + fileSystemName);
        }

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections,
            DokanFileInfo info)
        {
            OnFileRead(fileName);
            try
            {
                security = info.IsDirectory
                    ? (FileSystemSecurity)Directory.GetAccessControl(GetPath(fileName))
                    : File.GetAccessControl(GetPath(fileName));
                return Trace(nameof(GetFileSecurity), fileName, info, DokanResult.Success, sections.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                security = null;
                return Trace(nameof(GetFileSecurity), fileName, info, DokanResult.AccessDenied, sections.ToString());
            }
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections,
            DokanFileInfo info)
        {
            OnFileChanged(fileName);
            try
            {
                if (info.IsDirectory)
                {
                    Directory.SetAccessControl(GetPath(fileName), (DirectorySecurity)security);
                }
                else
                {
                    File.SetAccessControl(GetPath(fileName), (FileSecurity)security);
                }
                return Trace(nameof(SetFileSecurity), fileName, info, DokanResult.Success, sections.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                return Trace(nameof(SetFileSecurity), fileName, info, DokanResult.AccessDenied, sections.ToString());
            }
        }

        public NtStatus Mounted(DokanFileInfo info)
        {
            return Trace(nameof(Mounted), null, info, DokanResult.Success);
        }

        public NtStatus Unmounted(DokanFileInfo info)
        {
            return Trace(nameof(Unmounted), null, info, DokanResult.Success);
        }

        public NtStatus FindStreams(string fileName, IntPtr enumContext, out string streamName, out long streamSize,
            DokanFileInfo info)
        {
            streamName = string.Empty;
            streamSize = 0;
            return Trace(nameof(FindStreams), fileName, info, DokanResult.NotImplemented, enumContext.ToString(),
                "out " + streamName, "out " + streamSize.ToString());
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, DokanFileInfo info)
        {
            streams = new FileInformation[0];
            return Trace(nameof(FindStreams), fileName, info, DokanResult.NotImplemented);
        }

        public IList<FileInformation> FindFilesHelper(string fileName, string searchPattern)
        {
            OnFileRead(fileName);
            searchPattern = searchPattern.Replace('>', '?');
            searchPattern = searchPattern.Replace('<', '*');
            if (fileName == "\\")
            {
                lock (_lock)
                {
                    return projects.Where(x => MatchesPattern(x.Key, searchPattern)).Select(x => new FileInformation()
                    {
                        Attributes = FileAttributes.Directory,
                        FileName = x.Key
                    }).ToList();
                }
            }
            var k = GetPathAware(fileName);
            if (searchPattern.IndexOf('*') == -1 && searchPattern.IndexOf('?') == -1)
            {
                var path = Path.Combine(k, searchPattern);
                var attr = GetFileAttributes(path);
                if (attr == FileAttributes_NotFound) return Array.Empty<FileInformation>();
                FileSystemInfo finfo;
                if (IsDirectory(attr)) finfo = new DirectoryInfo(path);
                else finfo = new FileInfo(path);
                return new FileInformation[] {
                    new FileInformation
                    {
                        Attributes = finfo.Attributes,
                        CreationTime = finfo.CreationTime,
                        LastAccessTime = finfo.LastAccessTime,
                        LastWriteTime = finfo.LastWriteTime,
                        Length = (finfo as FileInfo)?.Length ?? 0,
                        FileName = finfo.Name
                    }
                };
            }

            IList<FileInformation> files = new DirectoryInfo(k)
                .GetFileSystemInfos(searchPattern)
                .Select(finfo => new FileInformation
                {
                    Attributes = finfo.Attributes,
                    CreationTime = finfo.CreationTime,
                    LastAccessTime = finfo.LastAccessTime,
                    LastWriteTime = finfo.LastWriteTime,
                    Length = (finfo as FileInfo)?.Length ?? 0,
                    FileName = finfo.Name
                }).ToArray();

            return files;
        }
        /*internal static bool InternalExists(string path)
        {
            Win32Native.WIN32_FILE_ATTRIBUTE_DATA wIN32_FILE_ATTRIBUTE_DATA = default(Win32Native.WIN32_FILE_ATTRIBUTE_DATA);
            return File.FillAttributeInfo(path, ref wIN32_FILE_ATTRIBUTE_DATA, false, true) == 0 && wIN32_FILE_ATTRIBUTE_DATA.fileAttributes != -1 && (wIN32_FILE_ATTRIBUTE_DATA.fileAttributes & 16) == 0;
        }*/

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern uint GetFileAttributes(string lpFileName);

        private static bool FileOrFolderExists(string path)
        {
            return GetFileAttributes(path) != FileAttributes_NotFound;
        }

        private const uint FileAttributes_NotFound = 0xFFFFFFFF;

        private bool MatchesPattern(string key, string searchPattern)
        {
            if (searchPattern == "*") return true;
            if (searchPattern.IndexOf('?') == -1 && searchPattern.IndexOf('*') == -1) return key.Equals(searchPattern, StringComparison.OrdinalIgnoreCase);
            return Regex.IsMatch(key, "^" + Regex.Escape(searchPattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase);
        }

        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files,
            DokanFileInfo info)
        {
            files = FindFilesHelper(fileName, searchPattern);

            return Trace(nameof(FindFilesWithPattern), fileName, info, DokanResult.Success);
        }

        public static BuildFsFileSystem Mount(char letter)
        {
            Dokan.Unmount(letter);

            var fs = new BuildFsFileSystem();
            fs.Letter = letter;
            var t = Task.Run(() => Dokan.Mount(fs, letter + ":", DokanOptions.DebugMode/* | DokanOptions.StderrOutput*/, 1));
            var e = Stopwatch.StartNew();
            while (e.ElapsedMilliseconds < 3000)
            {
                Thread.Sleep(200);
                if (t.Exception != null) throw t.Exception;
                if (Directory.Exists(letter + ":\\")) return fs;
            }
            return fs;
        }

        #endregion Implementation of IDokanOperations
    }
}