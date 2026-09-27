using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Common.Helpers;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.Interfaces;
using AOSharp.Core;
using AOSharp.Core.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MaliMissionRoller2
{
    public class Extensions
    {
        public static void ButtonSetGfx(Button button, int gfxId)
        {
            button.SetGfx(ButtonState.Raised, gfxId);
            button.SetGfx(ButtonState.Hover, gfxId);
            button.SetGfx(ButtonState.Pressed, gfxId);
        }

        public static string GetZoneName(int id)
        {
           return Utils.UnsafePointerToString(N3InterfaceModule_t.GetPFName(id));
        }

        public static void LoadCustomTextures(string path, int startId)
        {
            DirectoryInfo textureDir = new DirectoryInfo(path);

            foreach (var file in textureDir.GetFiles("*.png").OrderBy(x => x.Name))
            {
                GuiResourceManager.CreateGUITexture(file.Name.Replace(".png", "").Remove(0,4), startId++, file.FullName);
            }
        }

        public static int GetItemValue(Identity itemIdentity)
        {
            IntPtr pEngine = N3Engine_t.GetInstance();
            Identity none = Identity.None;
            IntPtr pItem = N3EngineClientAnarchy_t.GetItemByTemplate(pEngine, itemIdentity, ref none);
            return DummyItem_t.GetStat(pItem, AOSharp.Common.GameData.Stat.Value, 4);
        }

        public static unsafe string GetItemName(int lowId, int highId, int ql)
        {
            Identity none = Identity.None;
            IntPtr pEngine = N3Engine_t.GetInstance();

            if (!DummyItem.CreateDummyItemID(lowId, highId, ql, out Identity dummyItemId))
                throw new Exception($"Failed to create dummy item. LowId: {lowId}\tLowId: {highId}\tLowId: {ql}");

            IntPtr pItem = N3EngineClientAnarchy_t.GetItemByTemplate(pEngine, dummyItemId, ref none);

            if (pItem == IntPtr.Zero)
                throw new Exception($"DummyItem::DummyItem - Unable to locate item. LowId: {lowId}\tLowId: {highId}\tLowId: {ql}");

            return Utils.UnsafePointerToString((*(MemStruct*)pItem).Name);
        }

        public static void FormatItemDb(bool implants, bool refined, bool clusters, bool nanos, bool rest, bool showItemCount = false)
        {
            Main.ItemDb = new List<KeyValuePair<ItemInfo, List<Stat>>>();

            if (clusters)
                Main.ItemDb.AddRange(JsonConvert.DeserializeObject<List<KeyValuePair<ItemInfo, List<Stat>>>>(
                    File.ReadAllText($"{Main.PluginDir}\\JSON\\ItemDb_Clusters.json")));
            if (refined)
                Main.ItemDb.AddRange(JsonConvert.DeserializeObject<List<KeyValuePair<ItemInfo, List<Stat>>>>(
                    File.ReadAllText($"{Main.PluginDir}\\JSON\\ItemDb_Refined.json")));
            if (implants)
                Main.ItemDb.AddRange(JsonConvert.DeserializeObject<List<KeyValuePair<ItemInfo, List<Stat>>>>(
                    File.ReadAllText($"{Main.PluginDir}\\JSON\\ItemDb_Implants.json")));
            if (nanos)
                Main.ItemDb.AddRange(JsonConvert.DeserializeObject<List<KeyValuePair<ItemInfo, List<Stat>>>>(
                    File.ReadAllText($"{Main.PluginDir}\\JSON\\ItemDb_Nanos.json")));
            if (rest)
                Main.ItemDb.AddRange(JsonConvert.DeserializeObject<List<KeyValuePair<ItemInfo, List<Stat>>>>(
                    File.ReadAllText($"{Main.PluginDir}\\JSON\\ItemDb_Rest.json")));

            Main.ItemDb = Main.ItemDb.OrderBy(x => x.Key.Name).ToList();
            if (showItemCount)
                Chat.WriteLine($"Items loaded: {Main.ItemDb.Count}");
        }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 0)]
    internal struct MemStruct
    {
        [FieldOffset(0x14)]
        public Identity Identity;

        [FieldOffset(0x9C)]
        public IntPtr Name;
    }

    public class ItemInfo
    {
        public int LowId { get; set; }
        public int HighId { get; set; }
        public int LowQl { get; set; }
        public int HighQl { get; set; }
        public string[] Tags { get; set; }
        public string Name { get; set; }
    }
}
