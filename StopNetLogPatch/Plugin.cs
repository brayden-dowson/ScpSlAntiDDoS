using HarmonyLib;
using Newtonsoft.Json;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Enums;
using PluginAPI.Events;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using TheRiptide.Patches;

namespace TheRiptide
{
    public class Config
    {
        public bool Enabled { get; set; } = true;

        public string DiscordWebHook { get; set; } = "";
    }

    public class Plugin
    {
        public static Plugin Instance { get; private set; }

        [PluginConfig]
        public Config config;

        private Harmony harmony;
        private HttpClient client;

        [PluginEntryPoint("Anti-DDOS", "1.0.0", "", "The Riptide")]
        public void OnEnabled()
        {
            Log.Info("Enabling Anti-DDoS");
            Instance = this;
            harmony = new Harmony("TheRiptide.Anti-DDOS");
            if (config.Enabled)
            {
                EventManager.RegisterEvents(this);
                harmony.PatchAll();
                client = new HttpClient();
                Log.Info("Successfully Enabled Anti-DDoS");
            }
            else
                Log.Info("Anti-DDoS failed to start as its disabled in the Config");
        }

        [PluginUnload]
        public void OnDisabled()
        {
            Log.Info("Disabling Anti-DDoS");
            harmony.UnpatchAll("TheRiptide.Anti-DDOS");
            harmony = null;
            EventManager.UnregisterAllEvents(this);
            client.Dispose();
            client = null;
            Instance = null;
            Log.Info("Successfully Disabled Anti-DDoS");
        }

        [PluginEvent(ServerEventType.RoundEnd)]
        public void OnRoundEnd(RoundEndEvent e)
        {
            if(NetManagerOnMessageReceivedPatch.BadDataCount > 0)
            {
                int count = NetManagerOnMessageReceivedPatch.BadDataCount;
                long size = NetManagerOnMessageReceivedPatch.BadDataBytes;
                NetManagerOnMessageReceivedPatch.BadDataCount = 0;
                NetManagerOnMessageReceivedPatch.BadDataBytes = 0;

                Log.Info($"Potential DDoS detected\nPacket Count: {count}\nTotal Size: {BytesToString(size)}");

                if (string.IsNullOrEmpty(config.DiscordWebHook))
                {
                    Log.Info("Anti-DDoS config.DiscordWebHook empty");
                    return;
                }

                new Task(async () =>
                {
                    string name = JsonConvert.SerializeObject(ServerName());
                    string content = JsonConvert.SerializeObject($"Potential DDoS detected\nPacket Count: {count}\nTotal Size: {BytesToString(size)}");
                    string str = $@"
{{
  ""username"": {name},
  ""content"": {content}
}}
";

                    var str_content = new StringContent(str);
                    str_content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    var result = await client.PostAsync(config.DiscordWebHook, str_content);
                    if (!result.IsSuccessStatusCode)
                        Log.Error(await result.Content.ReadAsStringAsync());

                }).Start();
            }
        }


        //https://stackoverflow.com/questions/281640/how-do-i-get-a-human-readable-file-size-in-bytes-abbreviation-using-net
        static string BytesToString(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + suf[place];
        }

        private static string ServerName()
        {
            string server_name = StripTagsCharArray(GameCore.ConfigFile.ServerConfig.GetString("server_name", "My Server Name").Split('\n').First()).Replace("Discord", "");
            if (string.IsNullOrEmpty(server_name))
                server_name = "Server name Empty";

            return server_name;
        }

        public static string StripTagsCharArray(string source)
        {
            char[] array = new char[source.Length];
            int arrayIndex = 0;
            bool inside = false;
            for (int i = 0; i < source.Length; i++)
            {
                char let = source[i];
                if (let == '<')
                {
                    inside = true;
                    continue;
                }

                if (let == '>')
                {
                    inside = false;
                    continue;
                }

                if (!inside)
                {
                    array[arrayIndex] = let;
                    arrayIndex++;
                }
            }
            return new string(array, 0, arrayIndex);
        }
    }
}
