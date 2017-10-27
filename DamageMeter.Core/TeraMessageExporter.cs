﻿using DamageMeter.Sniffing;
using DamageMeter.TeraDpsApi;
using Data;
using Newtonsoft.Json;
using SevenZip;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tera;
using Tera.Game;
using Tera.Game.Messages;
using Tera.PacketLog;

namespace DamageMeter
{
    public class TeraMessageExporter
    {
        public static TeraMessageExporter Instance => _instance ?? (_instance = new TeraMessageExporter());
        private static TeraMessageExporter _instance;
        private static readonly string PUBLIC_KEY_STRING = "<?xml version=\"1.0\" encoding=\"utf-16\"?><RSAParameters xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Exponent>AQAB</Exponent><Modulus>sD+HLW7fz2xuQ+JoawSXsZLrb8m7Vn9HVnmkeIJazHDEwPycQrDyYo4XNI27qC2ZhEGlk0qQ1Dd8pFEvhsVVzyve2Ov7CuuuBm7I/rpO1ii9TvEPIjr47eQ5fY4+Trwzjp9au1nw8/E2XNJTFagU1Ch1jJK730BS3ZAbcJSnpUGR0svCnbAc2gpPUJfQxaQgYlr23bdS2dTC/qey/pieg9QhU4N9ZCoYMCshB5+r2wLEfcgHkYtP2aUbUBVGGQ4YtfkX8eIZsRjmMClEzeaVSqvkNh5q5K6qdKFpkc1zZnLKNhwjo/OmcjIc11q/8wlOZPiRKsVe9gC8ySdDCGQXIW9PF2rFYEvTVPWRVeLOPlCfTA1wVXDBlNs5Bchix7pBVumfO2apuizzgWfqm0Q7xyvsHfv7I7ejynjPr5/aEdHzWZK1/RSEwWCSMrstMTzDuuNgOlpYzbAxEpAc1APKAxxjD3C7bgY9IHFNgTpGIYlzJgA6xy2MCWgLm5q0pNjpaiQIBiuCArxMSIn2qpPOkoRLmi2cXHKl27WmjQtBVrw93jRPtLMUSyJ5fsXAVlXy5gnXBl69tQmrvuiRZKWqpZCDhrXHpUEj7J9cULUv0bjzonpAH6UnPVZTIp/VHq+yh0wnbPRUzqcT+ku34U8J3NGYlkf9ZgqGup9EJRka2eE=</Modulus></RSAParameters>";
        public static readonly List<AreaAllowed> BossAllowed = JsonConvert.DeserializeObject<List<AreaAllowed>>(
          "[{\"AreaId\": 735,\"BossIds\": [3000]},{\"AreaId\": 935,\"BossIds\": [3000]},{\"AreaId\": 950,\"BossIds\": [3000, 4000]},{\"AreaId\": 794,\"BossIds\": [Gaaruksalk]},{\"AreaId\": 994,\"BossIds\": [3000]},{\"AreaId\": 916,\"BossIds\": [1000]}]"
          );
        public void Export(EncounterBase teradpsData, NpcEntity entity)
        {
            // Only export when a notable dungeons is cleared
            var areaId = int.Parse(teradpsData.areaId);
            if (!BossAllowed.Any(x => x.AreaId == areaId && (x.BossIds.Count == 0 || x.BossIds.Contains((int)entity.Info.TemplateId)))) { return; }
            if (!TeraSniffer.Instance.EnableMessageStorage)
            {
                // Message storing have already been stopped
                return;
            }
            // Keep a local reference of the packet list
            Queue<Message> packetsCopyStorage = TeraSniffer.Instance.PacketsCopyStorage;

            // Stop filling the packet list & delete the original reference, so memory will be freed 
            // by the garbage collector after the export
            TeraSniffer.Instance.EnableMessageStorage = false;

            // Wait for thread to sync, more perf than concurrentQueue
            Thread.Sleep(1);

            var version = NetworkController.Instance.MessageFactory.Version;
            Guid id = Guid.NewGuid();
            string filename =  version + "_"+ id;

            Debug.WriteLine("Start exporting data");
            SaveToTmpFile(version.ToString(), packetsCopyStorage, filename+ ".TeraLog");
            Compress(filename + ".TeraLog", filename+".7z");
            File.Delete(filename + ".TeraLog");
            Encrypt(filename + ".7z", filename + ".rsa");
            File.Delete(filename + ".7z");
            //Send(filename + ".rsa");
            //File.Delete(filename+".rsa");

        }

        private static string RandomString(int length)
        {
            Random r = new Random();
            const string pool = "abcdefghijklmnopqrstuvwxyz0123456789";
            var chars = Enumerable.Range(0, length)
                .Select(x => pool[r.Next(0, pool.Length)]);
            return new string(chars.ToArray());
        }


        private void SaveToTmpFile(string version, Queue<Message> packetsCopyStorage, string filename)
        {
            var header = new LogHeader { Region = version };
            PacketLogWriter writer = new PacketLogWriter(filename, header);
            foreach (var message in packetsCopyStorage)
            {
                ParsedMessage parsedMessage = NetworkController.Instance.MessageFactory.Create(message);
                if(parsedMessage.GetType() == typeof(S_CHAT))
                {
                    ((S_CHAT)parsedMessage).Text = RandomString(((S_CHAT)parsedMessage).Text.Length);
                }else if(parsedMessage.GetType() == typeof(S_WHISPER))
                {
                    ((S_WHISPER)parsedMessage).Text = RandomString(((S_WHISPER)parsedMessage).Text.Length);
                }
                else if (parsedMessage.GetType() == typeof(S_PRIVATE_CHAT))
                {
                    ((S_PRIVATE_CHAT)parsedMessage).Text = RandomString(((S_PRIVATE_CHAT)parsedMessage).Text.Length);
                }
                //TODO add def for their C_ equivalent
                writer.Append(message);
            }
            writer.Dispose();
            
        }


        private void Encrypt(string inputFilename, string outputFilename)
        {
            var sr = new StringReader(PUBLIC_KEY_STRING);
            //we need a deserializer
            var xs = new System.Xml.Serialization.XmlSerializer(typeof(RSAParameters));
            //get the object back from the stream
            var publicKey = (RSAParameters)xs.Deserialize(sr);

            var csp = new RSACryptoServiceProvider();
            csp.ImportParameters(publicKey);

            var clearData = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, inputFilename));
            var encryptedData = csp.Encrypt(clearData, false);

            using (var fs = new FileStream(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, outputFilename), FileMode.Create, FileAccess.Write))
            {
                fs.Write(encryptedData, 0, encryptedData.Length);
            }

        }

        private void Compress(string inputFilename, string outputFilename)
        {
            var libpath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Environment.Is64BitProcess ? "lib/7z_x64.dll" : "lib/7z.dll");
            SevenZipBase.SetLibraryPath(libpath);
            var compressor = new SevenZipCompressor { ArchiveFormat = OutArchiveFormat.SevenZip };
            compressor.CustomParameters["tc"] = "off";
            compressor.CompressionLevel = CompressionLevel.Ultra;
            compressor.CompressionMode = CompressionMode.Create;
            compressor.TempFolderPath = Path.GetTempPath();
            compressor.PreserveDirectoryRoot = false;
            compressor.CompressFiles(outputFilename, new string[]{ Path.Combine(AppDomain.CurrentDomain.BaseDirectory,inputFilename)});
        }

        private void Send(string filename)
        {
            // TODO
        }

    }
}
