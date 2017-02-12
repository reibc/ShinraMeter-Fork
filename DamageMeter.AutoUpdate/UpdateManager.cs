﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DamageMeter.AutoUpdate
{
    public class UpdateManager
    {

        private static Dictionary<string, string> _hashes;
        private static Dictionary<string, string> _latest;
        public static string Version = "1.78";

        public static string ExecutableDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        public static void ReadDbVersion()
        {
            var version = Path.Combine(ResourcesDirectory, "head");
            try { if (File.Exists(version))
                Version=Version + "." + File.ReadLines(version).FirstOrDefault()?.Remove(7)??"";}
            catch { }// ignore bad head
        }

        public static string ResourcesDirectory
        {
            get
            {
                var directory = Path.GetDirectoryName(typeof(UpdateManager).Assembly.Location);
                while (directory != null)
                {
                    var resourceDirectory = Path.Combine(directory, @"resources\");
                    if (Directory.Exists(resourceDirectory))
                        return resourceDirectory;
                    directory = Path.GetDirectoryName(directory);
                }
                throw new InvalidOperationException("Could not find the resource directory");
            }
        }

        public static bool Update()
        {
            //Download(); return true;
            return HashedUpdate();
        }

        private static bool GetDiff(KeyValuePair<string, string> file)
        {
            using (var client = new WebClient())
            {
                var compressed = client.OpenRead(new Uri("https://neowutran.ovh/updates/ShinraMeterV/"+file.Key+".zip"));
                if (compressed == null) return true;
                new ZipArchive(compressed).Entries[0].ExtractToFile(ExecutableDirectory + @"\tmp\release\"+file.Key);
            }
            return FileHash(ExecutableDirectory + @"\tmp\release\" + file.Key) != file.Value;
        }

        internal static Dictionary<string, string> ReadHashFile(string file, string addPath="")
        {
            return File.ReadAllLines(file)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Split(new[] { " *" }, StringSplitOptions.None))
                .Select(parts => new KeyValuePair<string, string>(addPath + parts[1], parts[0])).ToDictionary(x => x.Key, x => x.Value);
        }

        private static bool HashedUpdate()
        {
            DestroyDownloadDirectory();
            Directory.CreateDirectory(ExecutableDirectory + @"\tmp\release\");
            var fileList = _latest.Except(_hashes).ToList();
            if (!fileList.Any())
            {
                return false;
            }
            File.WriteAllLines(ExecutableDirectory + @"\tmp\ShinraMeterV.sha1", _latest.Select(x => x.Value + " *" + x.Key));
            fileList.Where(x=>x.Key.Contains('\\')).Select(x=>Path.GetDirectoryName(ExecutableDirectory + @"\tmp\release\" + x.Key)).Distinct().ToList().ForEach(x=> Directory.CreateDirectory(x));
            bool badhash = false;
            fileList.ForEach(x=> badhash=badhash||GetDiff(x));
            if (badhash)
            {
                MessageBox.Show("Invalid checksum, abording upgrade");
                return false;
            }
            if (File.Exists(ExecutableDirectory + @"\tmp\release\Autoupdate.exe"))
                File.Copy(ExecutableDirectory + @"\tmp\release\Autoupdate.exe", ExecutableDirectory + @"\tmp\Autoupdate.exe");
            else
                File.Copy(ExecutableDirectory + @"\Autoupdate.exe", ExecutableDirectory + @"\tmp\Autoupdate.exe");
            Process.Start(ExecutableDirectory + @"\tmp\Autoupdate.exe", "pass");
            return true;
        }


        public static async Task<bool> IsUpToDate()
        {
            CurrentHash();
            return await NoNewHashes().ConfigureAwait(false);
            //var latestVersion = await LatestVersion().ConfigureAwait(false);
            //Console.WriteLine("Current version = " + Version);
            //Console.WriteLine("Latest version = " + latestVersion);
            //return latestVersion == Version;
        }

        private static void Decompress(string latestVersion)
        {
            // Get the stream of the source file.
            ZipFile.ExtractToDirectory(ExecutableDirectory + @"\tmp\" + latestVersion, ExecutableDirectory + @"\tmp\");
            ZipFile.ExtractToDirectory(ExecutableDirectory + @"\tmp\" + latestVersion, ExecutableDirectory + @"\tmp\release\");
        }


        private static void Download()
        {
            DestroyDownloadDirectory();
            Directory.CreateDirectory(ExecutableDirectory + @"\tmp\release\");


            var latestVersion = "ShinraMeterV" + LatestVersion().Result;
            Console.WriteLine("Downloading latest version");
            using (var client = new WebClient())
            {
                client.DownloadFile(
                    " https://neowutran.ovh/updates/" + latestVersion +
                    ".zip", ExecutableDirectory + @"\tmp\" + latestVersion + ".zip");
            }
            Console.WriteLine("Latest version downloaded");
            Console.WriteLine("Checksum");
            if (!Checksum(latestVersion, latestVersion + ".zip").Result)
            {
                MessageBox.Show("Invalid checksum, abording upgrade");
                return;
            }
            Console.WriteLine("Decompressing");
            Decompress(latestVersion + ".zip");
            Console.WriteLine("Decompressed");
            Process.Start(ExecutableDirectory + @"\tmp\" + latestVersion + @"\Autoupdate.exe", "pass");
            Console.WriteLine("Start upgrading");
        }

        private static void DestroyDownloadDirectory()
        {
            if (!Directory.Exists(ExecutableDirectory + @"\tmp\")) return;
            Directory.Delete(ExecutableDirectory + @"\tmp\", true);
        }

        public static void Copy(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                if (Path.GetFileName(file) == "ShinraLauncher.exe")
                {
                    if (File.Exists(Path.Combine(targetDir, Path.GetFileName(file)))) continue;
                }
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                if (directory == "config")
                {
                    Directory.CreateDirectory(targetDir);
                    continue;
                }
                Copy(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
            }
        }

        public static void DestroyRelease()
        {
            Array.ForEach(Directory.GetFiles(ExecutableDirectory + @"\..\..\").Where(t=>!t.EndsWith("ShinraLauncher.exe")).ToArray(), File.Delete);
            Array.ForEach(Directory.GetFiles(ExecutableDirectory + @"\..\..\resources\"), File.Delete);
            foreach (var s in Directory.GetDirectories(ExecutableDirectory + @"\..\..\").Where(t=> !(t.EndsWith("resources")||t.EndsWith("tmp"))))
            {
                if (Directory.Exists(s))
                {
                    Directory.Delete(s, true);
                }
            }
            if (!Directory.Exists(ExecutableDirectory + @"\..\..\resources\")) return;
            foreach (var s in Directory.GetDirectories(ExecutableDirectory + @"\..\..\resources\").Where(t => !t.EndsWith("config") && !t.EndsWith("sound")))
            {
                if (Directory.Exists(s))
                {
                    Directory.Delete(s, true);
                }
            }
            Console.WriteLine("Resources directory destroyed");
        }

        public static void DeleteEmptySubdirectories(string parentDirectory)
        {
            Parallel.ForEach(Directory.GetDirectories(parentDirectory), directory => {
                DeleteEmptySubdirectories(directory);
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
            });
        }

        public static void CleanupRelease(Dictionary<string,string> hashes)
        {
            Array.ForEach(Directory.GetFiles(ExecutableDirectory + @"\..\", "*", SearchOption.AllDirectories)
                .Where(t => !(t.Contains(@"\config\")||t.Contains(@"\..\tmp\")||t.Contains(@"\sound\")||t.EndsWith("ShinraLauncher.exe")||hashes.ContainsKey(t))).ToArray()
                ,x=> { File.Delete(x);Console.WriteLine(x); });
            DeleteEmptySubdirectories(ExecutableDirectory + @"\..\");
            Console.WriteLine("Obsolette files destroyed");
        }

        private static async Task<string> LatestVersion()
        {
            var version =
                await
                    GetResponseText("https://neowutran.ovh/updates/version.txt")
                        .ConfigureAwait(false);
            version = Regex.Replace(version, @"\r\n?|\n", "");

            return version;
        }

        private static async Task<bool> NoNewHashes()
        {
            using (var client = new WebClient())
            {
                var compressed = await client.OpenReadTaskAsync(new Uri("http://diclah.com/~yukikoo/ShinraMeterV.sha1.zip")).ConfigureAwait(false);
                if (compressed == null) return true;
                using (MemoryStream stream = new MemoryStream())
                {
                    new ZipArchive(compressed).Entries[0].Open().CopyTo(stream);
                    _latest=Encoding.UTF8.GetString(stream.ToArray()).Split(new string[] { "\r\n","\r","\n" }, StringSplitOptions.None)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Split(new[] { " *" }, StringSplitOptions.None))
                        .Select(parts => new KeyValuePair<string, string>(parts[1], parts[0])).ToDictionary(x => x.Key, x => x.Value);
                }
            }
            return _latest.Except(_hashes).Any()!=true;
        }

        public static string FileHash(string file)
        {
            string hashString;
            using (var stream = File.OpenRead(file))
            {
                var sha = SHA1.Create();
                var hash = sha.ComputeHash(stream);
                hashString = BitConverter.ToString(hash);
                hashString = hashString.Replace("-", "");
            }
            return hashString.ToLowerInvariant();
        }

        public static void CurrentHash()
        {
            _hashes=new Dictionary<string,string>();
            Array.ForEach(Directory.GetFiles(ExecutableDirectory,"*",SearchOption.AllDirectories).Where(t => 
                !t.EndsWith("ShinraLauncher.exe") && !t.Contains(@"\tmp\") && !t.Contains(@"\config\") && !t.Contains(@"\sound\") && !t.EndsWith("error.log")
                    ).ToArray(),x => _hashes.Add(x.Replace(ExecutableDirectory+"\\",""),FileHash(x)));
        }

        private static async Task<bool> Checksum(string version, string file)
        {
            var checksum =
                await
                    GetResponseText("http://diclah.com/~yukikoo/" + version + ".txt")
                        .ConfigureAwait(false);
            checksum = Regex.Replace(checksum, @"\r\n?|\n", "");
            string hashString;
            using (var stream = File.OpenRead(ExecutableDirectory + @"\tmp\" + file))
            {
                var sha512 = SHA512.Create();
                var hash = sha512.ComputeHash(stream);
                hashString = BitConverter.ToString(hash);
                hashString = hashString.Replace("-", "");
            }
            checksum = checksum.ToLowerInvariant();
            hashString = hashString.ToLowerInvariant();
            Console.WriteLine("Online checksum:" + checksum);
            Console.WriteLine("Computed checksum:" + hashString);
            return hashString == checksum;
        }

        private static async Task<string> GetResponseText(string address)
        {
            return await GetResponseText(address, 3);
        }

        private static async Task<string> GetResponseText(string address, int numbertry)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetByteArrayAsync(new Uri(address));
                    return Encoding.UTF8.GetString(response, 0, response.Length);
                }
            }
            catch (Exception)
            {
                if (numbertry > 0)
                {
                    return await GetResponseText(address, numbertry - 1);
                }
                throw;
            }
        }
    }
}