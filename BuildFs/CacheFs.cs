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
    internal class CacheFs : IDokanOperations
    {

        private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData |
                                              FileAccess.Execute |
                                              FileAccess.GenericExecute | FileAccess.GenericWrite |
                                              FileAccess.GenericRead;


        
        private const FileAccess DataWriteAccess = FileAccess.WriteData | FileAccess.AppendData |
                                                   FileAccess.Delete |
                                                   FileAccess.GenericWrite;

        private ConsoleLogger logger = new ConsoleLogger("[Mirror] ");

        public CacheFs()
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
                return "C:\\CacheFs" + fileName;
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

        internal void ReleaseOneHandle(Entry ent)
        {
            lock (InMemory)
            {
                ent.OpenHandles--;
            }
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
                            if (!Directory_Exists(filePath))
                            {
                                try
                                {
                                    if (!File_GetAttributes(filePath).HasFlag(FileAttributes.Directory))
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
                            if (Directory_Exists(filePath))
                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    attributes, DokanResult.FileExists);

                            var m = GetFileAttributes(filePath);
                            if(m != FileAttributes_NotFound && (m & (uint)FileAttributes.Directory) != 0)
                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                attributes, DokanResult.AlreadyExists);

                            OnFileChanged(fileName);
                            Directory_CreateDirectoryAssumeNotExisting(GetPath(fileName));
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
                    info.Context = FileStream_Open(filePath, mode,
                        readAccess ? System.IO.FileAccess.Read : System.IO.FileAccess.ReadWrite, share, 4096, options);

                    if (pathExists && (mode == FileMode.OpenOrCreate
                        || mode == FileMode.Create))
                        result = DokanResult.AlreadyExists;

                    if (mode == FileMode.CreateNew || mode == FileMode.Create) //Files are always created as Archive
                        attributes |= FileAttributes.Archive;
                    File_SetAttributes(filePath, attributes);
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

        private void Directory_CreateDirectoryAssumeNotExisting(string v)
        {
            var parent = Path.GetDirectoryName(v);
            lock (InMemory)
            {
                var entry = TryGetEntryHasLock(parent);
                if (entry != null)
                {
                    entry.Items.Add(Path.GetFileName(v));
                    var subentry = new Entry();
                    subentry.Attributes = FileAttributes.Directory;
                    subentry.LastWriteTimeUtc = DateTime.UtcNow;
                    subentry.Items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    InMemory[v] = subentry;
                }
                else
                {
                    OnPhysicalChange(v);
                    Directory.CreateDirectory(v);
                }
            }
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

            (info.Context as Stream)?.Dispose();
            info.Context = null;
            NtStatus result = NtStatus.Success;
            if (info.DeleteOnClose)
            {
                OnFileChanged(fileName);
                if (info.IsDirectory)
                {
                    result = Directory_TryDelete(GetPath(fileName));
                }
                else
                {
                    File_Delete(GetPath(fileName));
                }
            }
            Trace(nameof(Cleanup), fileName, info, result);
        }

        private NtStatus Directory_TryDelete(string physicalPath)
        {
            NtStatus result = NtStatus.DirectoryNotEmpty;
            lock (InMemory)
            {
                var entry = TryGetEntryHasLock(physicalPath);
                if (entry != null)
                {
                    if (entry.Attributes == default(FileAttributes) || entry.Items.Count == 0)
                    {
                        entry.Attributes = default(FileAttributes);
                        entry.Items = null;
                        result = NtStatus.Success;
                    }
                }
                else
                {
                    if (!Directory.Exists(physicalPath))
                    {
                        result = NtStatus.Success;
                    }
                    else if(!Directory.EnumerateFiles(physicalPath).Any())
                    {
                        Directory.Delete(physicalPath);
                        result = NtStatus.Success;
                    }
                }
                var parent = TryGetEntryHasLock(Path.GetDirectoryName(physicalPath));
                if (parent != null)
                {
                    parent.Items.Remove(Path.GetFileName(physicalPath));
                }
            }
            return result;
        }

        public void CloseFile(string fileName, DokanFileInfo info)
        {
#if TRACE
            if (info.Context != null)
                Console.WriteLine(DokanFormat($"{nameof(CloseFile)}('{fileName}', {info} - entering"));
#endif

            (info.Context as Stream)?.Dispose();
            info.Context = null;
            Trace(nameof(CloseFile), fileName, info, DokanResult.Success);
            // could recreate cleanup code here but this is not called sometimes
        }


        private Dictionary<string, Entry> InMemory = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private uint GetFileAttributes(string physicalPath)
        {
            lock (InMemory)
            {
                var entry = TryGetEntryHasLock(physicalPath);
                if (entry != null) 
                {
                    if (entry.Attributes == default(FileAttributes)) return FileAttributes_NotFound;
                    return (uint)entry.Attributes;
                }
                return NativeMethods.GetFileAttributes(physicalPath);
            }
        }


        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, DokanFileInfo info)
        {
            OnFileRead(fileName);
            if (info.Context == null) // memory mapped read
            {
                using (var stream = FileStream_Open(GetPath(fileName), FileMode.Open, System.IO.FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    stream.Position = offset;
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
            }
            else // normal read
            {
                var stream = info.Context as Stream;
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
            //Console.WriteLine("Change: " + fileName);
        }

        private void OnFileRead(string fileName)
        {
        }
        private void SaveRepositoryStatus()
        {
            lock (_lock)
            {
            }
        }
        
        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            if (info.Context == null)
            {
                using (var stream = FileStream_Open(GetPath(fileName), FileMode.Open, System.IO.FileAccess.Write, FileShare.Delete))
                {
                    stream.Position = offset;
                    stream.Write(buffer, 0, buffer.Length);
                    bytesWritten = buffer.Length;
                }
            }
            else
            {
                var stream = info.Context as Stream;
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

        private Stream FileStream_Open(string path, FileMode mode, System.IO.FileAccess fileAccess, FileShare fileShare, int bufferSize = 4096, FileOptions options = FileOptions.None)
        {
            lock (InMemory)
            {
                var firstUse = false;
                var entry = TryGetEntryHasLock(path);
                if (entry == null || entry.Attributes == default(FileAttributes))
                {
                    if (fileAccess == System.IO.FileAccess.Read && mode == FileMode.CreateNew) fileAccess = System.IO.FileAccess.ReadWrite;
                    if (fileAccess == System.IO.FileAccess.Read) return new FileStream(path, mode, fileAccess, fileShare, bufferSize, options);
                    firstUse = true;
                    entry = new Entry();
                    entry.Attributes = FileAttributes.Archive;
                    entry.LastWriteTimeUtc = DateTime.UtcNow;
                    entry.Contents = new MemoryStream();
                    InMemory[path] = entry;

                    var parentDirectory = Path.GetDirectoryName(path);
                    var parent = TryGetEntryHasLock(parentDirectory);
                    if (parent != null)
                    {
                        parent.Items.Add(Path.GetFileName(path));
                    }
                    else
                    {
                        if (!File.Exists(path))
                        {
                            parent = new Entry();
                            parent.Attributes = (FileAttributes)GetFileAttributes(parentDirectory);
                            parent.Items = new HashSet<string>(Directory.EnumerateFileSystemEntries(parentDirectory).Select(x => Path.GetFileName(x)), StringComparer.OrdinalIgnoreCase);
                            parent.Items.Add(Path.GetFileName(path));
                            InMemory[parentDirectory] = parent;
                        }
                    }
                    
                }

                if (mode == FileMode.Truncate || mode == FileMode.Create || mode == FileMode.CreateNew) entry.Contents.SetLength(0);
                var stream = new EntryStream(entry, fileAccess);
                if (firstUse && (mode == FileMode.Open || mode == FileMode.OpenOrCreate || mode == FileMode.Append))
                {
                    if (File.Exists(path))
                    {
                        Console.WriteLine("LOAD: " + path);
                        using (var original = File.Open(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Delete | FileShare.ReadWrite))
                        {
                            original.CopyTo(entry.Contents);
                        }
                    }
                }
                if (mode == FileMode.Append) stream.Position = entry.Length;
                entry.FileSystem = this;
                entry.OpenHandles++;
                return stream;
            }
        }

        public NtStatus FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            try
            {
                ((Stream)(info.Context)).Flush();
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
            fileInfo = GetFileInformation(fileName, attr);
            return Trace(nameof(GetFileInformation), fileName, info, DokanResult.Success);
        }

        private FileInformation GetFileInformation(string virtualPath, uint attr)
        {
            var physicalPath = GetPath(virtualPath);
            lock (InMemory)
            {
                var entry = TryGetEntryHasLock(physicalPath);
                if (entry != null)
                {
                    return new FileInformation
                    {
                        FileName = Path.GetFileName(virtualPath),
                        Attributes = entry.Attributes,
                        LastWriteTime = entry.LastWriteTimeUtc,
                        Length = IsDirectory((uint)entry.Attributes) ? 0 : entry.Length
                    };
                }
                else
                {
                    var finfo = IsDirectory(attr) ? new DirectoryInfo(physicalPath) : (FileSystemInfo)new FileInfo(physicalPath);

                    return new FileInformation
                    {
                        FileName = Path.GetFileName(virtualPath),
                        Attributes = finfo.Attributes,
                        CreationTime = finfo.CreationTime,
                        LastAccessTime = finfo.LastAccessTime,
                        LastWriteTime = finfo.LastWriteTime,
                        Length = (finfo as FileInfo)?.Length ?? 0,
                    };
                }
                
            }
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
                File_SetAttributes(GetPath(fileName), attributes);
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

        private Entry TryGetEntryHasLock(string path)
        {
            Entry entry;
            path = NormalizePath(path);
            InMemory.TryGetValue(path, out entry);
            return entry;
        }

        private void SetEntryHasLock(string path, Entry entry)
        {
            InMemory[NormalizePath(path)] = entry;
        }

        private string NormalizePath(string path)
        {
            if (path[0] == '\\') throw new ArgumentException();
            path = path.Replace('/', '\\');
            
            if (path.Contains("\\\\") || path.Contains(@"\.\") || path.Contains(@"\..\"))
            {
                path = Path.GetFullPath(path);
            }
            
            if (path.EndsWith("\\")) path = path.Substring(0, path.Length - 1);
            return path;
        }

        private void File_SetAttributes(string v, FileAttributes attributes)
        {
            lock (InMemory)
            {
                var entry = TryGetEntryHasLock(v);
                if (entry != null)
                {
                    if (attributes == default(FileAttributes)) attributes = FileAttributes.Normal;
                    entry.Attributes = attributes;
                }
                else
                {
                    OnPhysicalChange(v); ;
                    File.SetAttributes(v, attributes);
                }
            }
            
        }

        private void OnPhysicalChange(string v)
        {
            Console.WriteLine("Physical change: " + v);
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
            DateTime? lastWriteTime, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            try
            {
                var filePath = GetPath(fileName);
                if (creationTime.HasValue)
                    File_SetCreationTime(filePath, creationTime.Value);

                if (lastAccessTime.HasValue)
                    File_SetLastAccessTime(filePath, lastAccessTime.Value);

                if (lastWriteTime.HasValue)
                    File_SetLastWriteTime(filePath, lastWriteTime.Value);

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

        private void File_SetLastWriteTime(string filePath, DateTime value)
        {
            lock (InMemory)
            {
                var entry = TryGetEntryHasLock(filePath);
                if (entry != null)
                {
                    entry.LastWriteTimeUtc = value;
                }
                else
                {
                    File.SetLastWriteTimeUtc(filePath, value);
                }
            }
        }

        private void File_SetLastAccessTime(string filePath, DateTime value)
        {
            //throw new NotImplementedException();
        }

        private void File_SetCreationTime(string filePath, DateTime value)
        {
            throw new NotImplementedException();
        }

        public NtStatus DeleteFile(string fileName, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            var filePath = GetPath(fileName);

            if (Directory_Exists(filePath))
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.AccessDenied);

            if (!File_Exists(filePath))
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.FileNotFound);

            if (File_GetAttributes(filePath).HasFlag(FileAttributes.Directory))
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.AccessDenied);

            return Trace(nameof(DeleteFile), fileName, info, DokanResult.Success);
            // we just check here if we could delete the file - the true deletion is in Cleanup
        }

        private FileAttributes File_GetAttributes(string filePath)
        {
            var m = GetFileAttributes(filePath);
            if (m == FileAttributes_NotFound) throw new FileNotFoundException();
            return (FileAttributes)m;
        }

        public NtStatus DeleteDirectory(string fileName, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            var result = Directory_TryDelete(GetPath(fileName));
            return Trace(nameof(DeleteDirectory), fileName, info, result);
        }

        
        public NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            OnFileRead(oldName);
            OnFileChanged(oldName);
            OnFileChanged(newName);
            var oldpath = GetPath(oldName);
            var newpath = GetPathAware(newName);
            if (newpath == null) return DokanResult.PathNotFound;

            (info.Context as Stream)?.Dispose();
            info.Context = null;

            var exist = info.IsDirectory ? Directory_Exists(newpath) : File_Exists(newpath);

            try
            {

                if (!exist)
                {
                    info.Context = null;
                    if (info.IsDirectory)
                        Directory_Move(oldpath, newpath);
                    else
                        File_Move(oldpath, newpath);
                    return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName,
                        replace.ToString(CultureInfo.InvariantCulture));
                }
                else if (replace)
                {
                    info.Context = null;

                    if (info.IsDirectory) //Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
                        return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName,
                            replace.ToString(CultureInfo.InvariantCulture));

                    File_Delete(newpath);
                    File_Move(oldpath, newpath);
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


        private void File_Move(string oldpath, string newpath)
        {
            FileOrDirectory_Move(oldpath, newpath, false);
        }
        private void FileOrDirectory_Move(string oldpath, string newpath, bool isDirectory)
        {
            lock (InMemory)
            {
                var entry = TryGetEntryHasLock(oldpath);
                var replaced = TryGetEntryHasLock(newpath);
                if (replaced != null && replaced.Attributes != default(FileAttributes))
                {
                    throw new InvalidOperationException();
                }
                if (entry != null)
                {
                    if (entry.Attributes == default(FileAttributes)) throw new FileNotFoundException();
                    if (!isDirectory)
                    {
                        InMemory[newpath] = entry;
                        InMemory[oldpath] = new Entry() { };
                    }
                }
                else
                {
                    OnPhysicalChange(oldpath);
                    OnPhysicalChange(newpath);
                    if (replaced != null)
                    {
                        if (isDirectory)
                        {
                            try
                            {
                                Directory.Delete(newpath);
                            }
                            catch (Exception)
                            {
                                File.Delete(newpath);
                            }
                        }
                        else
                        {
                            try
                            {
                                File.Delete(newpath);
                            }
                            catch (Exception)
                            {
                                Directory.Delete(newpath);
                            }
                        }
                        InMemory[newpath] = null;
                    }

                    if (isDirectory)
                    {
                        Directory.Move(oldpath, newpath);
                    }
                    else
                    {
                        File.Move(oldpath, newpath);
                        InMemory.Remove(newpath);
                    }
                }
                var parentDirectory1 = TryGetEntryHasLock( Path.GetDirectoryName(oldpath));
                var parentDirectory2 = TryGetEntryHasLock( Path.GetDirectoryName(newpath));

                if (parentDirectory1 != null) parentDirectory1.Items.Remove(Path.GetFileName(oldpath));
                if (parentDirectory2 != null) parentDirectory2.Items.Add(Path.GetFileName(newpath));

                if (isDirectory)
                {
                    foreach (var item in InMemory.ToList())
                    {
                        var key = item.Key;
                        if (
                            key.Equals(oldpath, StringComparison.OrdinalIgnoreCase) ||
                            (key.StartsWith(oldpath, StringComparison.OrdinalIgnoreCase) && key.Length > oldpath.Length && key[oldpath.Length] == '\\'))
                        {
                            InMemory.Remove(key);
                            InMemory[newpath + key.Substring(oldpath.Length)] = item.Value;
                        }
                    }
                }

            }
        }

        private void File_Delete(string path)
        {
            lock (InMemory)
            {
                var entry = TryGetEntryHasLock(path);
                var parentPath = Path.GetDirectoryName(path);
                var parent = TryGetEntryHasLock(parentPath);

                if (entry != null)
                {
                    entry.Attributes = default(FileAttributes);
                    entry.Contents?.Dispose();
                    entry.Contents = null;
                    if (!File.Exists(path))
                    {
                        InMemory.Remove(path);
                    }
                }
                else
                {
                    OnPhysicalChange(path);
                    File.Delete(path);
                }

                if (parent != null)
                {
                    parent.Items.Remove(Path.GetFileName(path));
                }
            }
        }

        private void Directory_Move(string oldpath, string newpath)
        {
            FileOrDirectory_Move(oldpath, newpath, true);
        }

        private bool File_Exists(string path)
        {
            var m = GetFileAttributes(path);
            return m != FileAttributes_NotFound && ((m & (uint)FileAttributes.Directory) == 0);
        }
        private bool Directory_Exists(string path)
        {
            var m = GetFileAttributes(path);
            return m != FileAttributes_NotFound && ((m & (uint)FileAttributes.Directory) != 0);
        }

        public NtStatus SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            OnFileChanged(fileName);
            try
            {
                ((Stream)(info.Context)).SetLength(length);
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
                ((Stream)(info.Context)).SetLength(length);
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
            Console.WriteLine("Lock: " + fileName);
            try
            {
                (info.Context as FileStream).Lock(offset, length);
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
            Console.WriteLine("Unlock: " + fileName);
            try
            {
                (info.Context as FileStream).Unlock(offset, length);
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
            volumeLabel = "CacheFs";
            fileSystemName = "CacheFs";

            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
                       FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage |
                       FileSystemFeatures.UnicodeOnDisk;

            return Trace(nameof(GetVolumeInformation), null, info, DokanResult.Success, "out " + volumeLabel,
                "out " + features.ToString(), "out " + fileSystemName);
        }

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections,
            DokanFileInfo info)
        {
            security = null;
            return NtStatus.NotImplemented;
#if false
            OnFileRead(fileName);
            try
            {
                security = info.IsDirectory
                    ? (FileSystemSecurity)Directory_GetAccessControl(GetPath(fileName))
                    : File_GetAccessControl(GetPath(fileName));
                return Trace(nameof(GetFileSecurity), fileName, info, DokanResult.Success, sections.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                security = null;
                return Trace(nameof(GetFileSecurity), fileName, info, DokanResult.AccessDenied, sections.ToString());
            }
#endif
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections,
            DokanFileInfo info)
        {
            return NtStatus.Success;
#if false
            OnFileChanged(fileName);
            try
            {
                if (info.IsDirectory)
                {
                    Directory_SetAccessControl(GetPath(fileName), (DirectorySecurity)security);
                }
                else
                {
                    File_SetAccessControl(GetPath(fileName), (FileSecurity)security);
                }
                return Trace(nameof(SetFileSecurity), fileName, info, DokanResult.Success, sections.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                return Trace(nameof(SetFileSecurity), fileName, info, DokanResult.AccessDenied, sections.ToString());
            }
#endif
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
            if (searchPattern.Contains("command-save"))
            {
                SaveChanges((path, entry) => false);
            }
            searchPattern = searchPattern.Replace('>', '?');
            searchPattern = searchPattern.Replace('<', '*');
            /*if (fileName == "\\")
            {
                lock (_lock)
                {
                    return Array.Empty<FileInformation>();
                }
            }*/
            var k = GetPathAware(fileName);
            if (searchPattern.IndexOf('*') == -1 && searchPattern.IndexOf('?') == -1)
            {
                var path = Path.Combine(k, searchPattern);
                var attr = GetFileAttributes(path);
                if (attr == FileAttributes_NotFound) return Array.Empty<FileInformation>();

                return new[] { GetFileInformation(Path.Combine(fileName, searchPattern), attr) };
            }

            lock (InMemory)
            {
                var physicalPath = GetPath(fileName);
                var entry = TryGetEntryHasLock(physicalPath);
                if (entry != null)
                {
                    return entry.Items.Where(x => MatchesPattern(x, searchPattern)).Select(x => 
                    {
                        var m = Path.Combine(fileName, x);
                        return GetFileInformation(m, GetFileAttributes(GetPath(m)));
                    }).ToArray();
                }
                else
                {

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
            }

        }

        public void SaveChanges(Func<string, Entry, bool> shouldIgnoreFile)
        {
            lock (InMemory)
            {
                var stillInUse = new List<KeyValuePair<string, Entry>>();
                foreach (var item in InMemory)
                {
                    var physicalPath = item.Key;
                    var entry = item.Value;


                    var attr = NativeMethods.GetFileAttributes(physicalPath);
                    if (attr != FileAttributes_NotFound && (entry.Attributes == default(FileAttributes) || IsDirectory((uint)entry.Attributes) != IsDirectory(attr)))
                    {
                        Console.WriteLine("Deleted: " + physicalPath);
                        if (IsDirectory(attr)) Directory.Delete(physicalPath);
                        else File.Delete(physicalPath);
                    }

                    if (entry.OpenHandles != 0)
                    {
                        stillInUse.Add(item);
                    }

                    if (entry.Contents != null)
                    {
                        if (shouldIgnoreFile(physicalPath, entry)) continue;
                        Console.WriteLine("Written: " + physicalPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath));
                        var tempName = physicalPath + ".shaman-fs-tmp.tmp";
                        using (var f = File.Open(tempName, FileMode.Create, System.IO.FileAccess.Write, FileShare.Delete))
                        {
                            var cpy = new MemoryStream(entry.Contents.GetBuffer(), 0, (int)entry.Contents.Length, false);
                            cpy.CopyTo(f);
                        }
                        File.Delete(physicalPath);
                        File.SetLastWriteTimeUtc(tempName, entry.LastWriteTimeUtc);
                        File.Move(tempName, physicalPath);
                        attr = (uint)FileAttributes.Normal;
                    }


                    if (entry.Attributes != default(FileAttributes) && attr != (uint)entry.Attributes)
                    {
                        Console.WriteLine("Changed attributes: " + physicalPath);
                        if (attr == FileAttributes_NotFound && IsDirectory((uint)entry.Attributes)) Directory.CreateDirectory(physicalPath);
                        File.SetAttributes(physicalPath, entry.Attributes);
                    }
                    
                    
                }
                InMemory.Clear();
                foreach (var item in stillInUse)
                {
                    Console.WriteLine("Still in use: "+item.Key);
                    InMemory.Add(item.Key, item.Value);
                }
            }
        }


        public void DeleteInMemoryFiles(Func<string, Entry, bool> canDeleteFile, int targetMemoryUsage)
        {
            lock (InMemory)
            {
                var totalBytes = GetInMemoryUsage();
                foreach (var item in InMemory.OrderByDescending(x=>(x.Value.Contents?.Capacity).GetValueOrDefault()).ToList())
                {
                    if (totalBytes < targetMemoryUsage) break;
                    var entry = item.Value;
                    if (entry.Attributes != default(FileAttributes) && !IsDirectory((uint)entry.Attributes))
                    {
                        if (canDeleteFile(item.Key, entry))
                        {
                            Console.WriteLine("Remove from cache: " + item.Key);
                            totalBytes -= item.Value.Contents.Capacity;
                            if (entry.OpenHandles == 0) entry.Contents.Dispose();
                            InMemory.Remove(item.Key);
                        }
                    }
                }
            }
        }

        public int GetInMemoryUsage()
        {
            lock (InMemory)
            {
                return InMemory.Sum(x => (x.Value.Contents?.Capacity).GetValueOrDefault());
            }
        }

        /*internal static bool InternalExists(string path)
{
   Win32Native.WIN32_FILE_ATTRIBUTE_DATA wIN32_FILE_ATTRIBUTE_DATA = default(Win32Native.WIN32_FILE_ATTRIBUTE_DATA);
   return File.FillAttributeInfo(path, ref wIN32_FILE_ATTRIBUTE_DATA, false, true) == 0 && wIN32_FILE_ATTRIBUTE_DATA.fileAttributes != -1 && (wIN32_FILE_ATTRIBUTE_DATA.fileAttributes & 16) == 0;
}*/


        private bool FileOrFolderExists(string path)
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

        public static CacheFs Mount(char letter)
        {
            Dokan.Unmount(letter);

            var fs = new CacheFs();
            fs.Letter = letter;
            var t = Task.Run(() => Dokan.Mount(fs, letter + ":", DokanOptions.DebugMode /*| DokanOptions.StderrOutput*/, 1));
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