using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Mathf
    {
        public static int FloorToInt(float value) => (int)Math.Floor(value);
        public static float Min(float a, float b) => Math.Min(a, b);
    }
}
namespace RimWorld { }
namespace Verse
{
    public enum ThingCategory { Building }
    public enum FillCategory { None, Full }
    public enum LoadSaveMode { Inactive, Saving, PostLoadInit }
    public enum LookMode { Def }
    public sealed class BuildingProperties
    {
        public bool isNaturalRock, isResourceRock;
        public ThingDef mineableThing;
    }
    public class ThingDef
    {
        public ThingCategory category;
        public FillCategory Fillage;
        public BuildingProperties building;
        public bool mineable, IsDoor;
    }
    public class Thing { public ThingDef def; }
    public class Blueprint : Thing { }
    public class Frame : Thing { }
    public class RoofDef { public bool isNatural, isThickRoof; }
    public struct IntVec3
    {
        public int x, y, z;
        public IntVec3(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
    }
    public sealed class Room { public bool ProperRoom; }
    public sealed class TerrainDef { public bool dontRender; }
    public sealed class EdificeGrid
    {
        public Thing[] Data;
        public Thing this[int index] => Data[index];
    }
    public sealed class RoofGrid
    {
        public RoofDef[] Data;
        public int Width;
        public RoofDef RoofAt(int index) => Data[index];
        public RoofDef RoofAt(IntVec3 cell) => Data[cell.z * Width + cell.x];
    }
    public sealed class TerrainGrid { public TerrainDef TerrainAt(IntVec3 cell) => null; }
    public sealed class RegionAndRoomUpdater
    {
        public bool Enabled = true;
        public bool AnythingToRebuild;
        public Action Rebuild;
        public void TryRebuildDirtyRegionsAndRooms()
        {
            if (!Enabled || !AnythingToRebuild) return;
            Rebuild?.Invoke();
            AnythingToRebuild = false;
        }
    }
    public sealed class MapEvents
    {
        public event Action RegionsRoomsChanged;
        public void Notify_RegionsRoomsChanged() => RegionsRoomsChanged?.Invoke();
    }
    public sealed class Map
    {
        public bool Disposed;
        public IntVec3 Size;
        public int uniqueID = 1;
        public EdificeGrid edificeGrid;
        public RoofGrid roofGrid;
        public TerrainGrid terrainGrid = new TerrainGrid();
        public MapEvents events = new MapEvents();
        public RegionAndRoomUpdater regionAndRoomUpdater = new RegionAndRoomUpdater();
        public Room[] Rooms;
        public Action BeforeRoomQuery;
        public Map(int width, int depth)
        {
            Size = new IntVec3(width, 0, depth);
            edificeGrid = new EdificeGrid { Data = new Thing[width * depth] };
            roofGrid = new RoofGrid { Data = new RoofDef[width * depth], Width = width };
            Rooms = new Room[width * depth];
        }
    }
    public static class GridsUtility
    {
        public static bool InBounds(this IntVec3 cell, Map map) => cell.x >= 0 && cell.z >= 0 && cell.x < map.Size.x && cell.z < map.Size.z;
        public static Thing GetEdifice(this IntVec3 cell, Map map) => map.edificeGrid[cell.z * map.Size.x + cell.x];
        public static Room GetRoom(this IntVec3 cell, Map map)
        {
            map.BeforeRoomQuery?.Invoke();
            return map.Rooms[cell.z * map.Size.x + cell.x];
        }
    }
    public class MapComponent
    {
        protected Map map;
        public MapComponent(Map map) { this.map = map; }
        public virtual void ExposeData() { }
        public virtual void MapGenerated() { }
        public virtual void FinalizeInit() { }
    }
    public static class Scribe { public static LoadSaveMode mode; }
    public static class Scribe_Values
    {
        public static void Look(ref string value, string key) { }
        public static void Look(ref int value, string key, int defaultValue) { }
    }
    public static class Scribe_Collections { public static void Look<T>(ref List<T> value, string key, LookMode mode) { } }
    public static class DefDatabase<T> { public static List<T> AllDefsListForReading = new List<T>(); }
    public static class GenAdj { public static IEnumerable<IntVec3> OccupiedRect(Thing thing) => Array.Empty<IntVec3>(); }
}
namespace RimFirstPerson3D
{
    public static class LevelStack3D { public static List<Verse.Map> StackMapsAt(Verse.Map map) => new List<Verse.Map> { map }; }
    public static class Fps3DState { public const float StoryHeight = 5f; }
}
