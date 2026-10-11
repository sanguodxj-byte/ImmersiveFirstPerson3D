using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    public sealed class MountainField3D : MapComponent
    {
        private bool initialized;
        private bool shapeDirty;
        private bool capsDirty = true;
        private bool[] footprint;
        private ushort[] rockKinds;
        private List<ThingDef> rockDefs = new List<ThingDef>();
        private string savedKinds;
        private int seed;
        private readonly HashSet<int> pending = new HashSet<int>();
        private readonly List<int> changed = new List<int>();
        private readonly List<Map> above = new List<Map>();
        private readonly List<float> aboveY = new List<float>();
        private int upperSignature;

        public MountainShape3D Shape { get; private set; }
        public byte[] Cells { get; private set; }
        public float[] Ceilings { get; private set; }
        public IReadOnlyList<int> Changed => changed;
        public bool ShapeChanged { get; private set; }
        public bool Ready => initialized && Shape != null;
        public float LastBuildMs { get; private set; }

        public MountainField3D(Map map) : base(map) { }

        public static bool IsRock(Thing t)
        {
            return t != null && IsRock(t.def) && !(t is Blueprint) && !(t is Frame);
        }

        public static bool IsRock(ThingDef def)
        {
            return def != null && def.category == ThingCategory.Building
                && def.Fillage == FillCategory.Full && def.building != null
                && (def.building.isNaturalRock || (def.mineable && def.building.mineableThing != null));
        }

        private static bool NaturalRoof(RoofDef roof) => roof != null && roof.isNatural;

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving && initialized)
            {
                var bytes = new byte[rockKinds.Length * 2];
                for (int i = 0; i < rockKinds.Length; i++)
                {
                    bytes[i * 2] = (byte)rockKinds[i];
                    bytes[i * 2 + 1] = (byte)(rockKinds[i] >> 8);
                }
                savedKinds = Convert.ToBase64String(bytes);
            }
            Scribe_Values.Look(ref savedKinds, "fp3dMountainKinds");
            Scribe_Values.Look(ref seed, "fp3dMountainSeed", 0);
            Scribe_Collections.Look(ref rockDefs, "fp3dMountainRocks", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && rockDefs == null)
                rockDefs = new List<ThingDef>();
        }

        public override void MapGenerated() => Capture();
        public override void FinalizeInit() => Capture();

        private ushort CodeFor(ThingDef def)
        {
            int i = rockDefs.IndexOf(def);
            if (i >= 0) return (ushort)(i + 1);
            if (def == null || rockDefs.Count >= ushort.MaxValue) return 0;
            rockDefs.Add(def);
            return (ushort)rockDefs.Count;
        }

        private ThingDef DefaultRock()
        {
            for (int i = 0; i < rockDefs.Count; i++)
                if (IsRock(rockDefs[i]) && !rockDefs[i].building.isResourceRock) return rockDefs[i];
            var defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
                if (IsRock(defs[i]) && !defs[i].building.isResourceRock) return defs[i];
            return rockDefs.Count > 0 ? rockDefs[0] : null;
        }

        private void Capture()
        {
            if (initialized || map == null || map.Disposed || map.Size.x <= 0 || map.Size.z <= 0) return;
            int count = map.Size.x * map.Size.z;
            footprint = new bool[count];
            rockKinds = new ushort[count];
            Cells = new byte[count];
            Ceilings = new float[count];
            if (rockDefs == null) rockDefs = new List<ThingDef>();
            if (!string.IsNullOrEmpty(savedKinds))
            {
                try
                {
                    var bytes = Convert.FromBase64String(savedKinds);
                    if (bytes.Length == count * 2)
                        for (int i = 0; i < count; i++)
                        {
                            int code = bytes[i * 2] | bytes[i * 2 + 1] << 8;
                            if (code > 0 && code <= rockDefs.Count)
                            {
                                rockKinds[i] = (ushort)code;
                                footprint[i] = true;
                            }
                        }
                }
                catch (FormatException) { }
            }
            for (int i = 0; i < count; i++)
            {
                Ceilings[i] = float.PositiveInfinity;
                int x = i % map.Size.x, z = i / map.Size.x;
                var c = new IntVec3(x, 0, z);
                var ed = c.GetEdifice(map);
                bool rock = IsRock(ed);
                bool roof = NaturalRoof(map.roofGrid.RoofAt(c));
                if (rock)
                {
                    rockKinds[i] = CodeFor(ed.def);
                    footprint[i] = true;
                }
                if (roof) footprint[i] = true;
                Cells[i] = rock ? (byte)1 : roof ? (byte)2 : (byte)0;
            }
            FillRoofMaterials();
            if (seed == 0) seed = unchecked(map.uniqueID * 73856093 + 19349663);
            initialized = true;
            shapeDirty = true;
        }

        private void FillRoofMaterials()
        {
            var queue = new int[rockKinds.Length];
            int read = 0, write = 0;
            for (int i = 0; i < rockKinds.Length; i++)
                if (footprint[i] && rockKinds[i] != 0) queue[write++] = i;
            while (read < write)
            {
                int i = queue[read++], x = i % map.Size.x, z = i / map.Size.x;
                if (x > 0) Fill(i - 1, rockKinds[i], queue, ref write);
                if (x + 1 < map.Size.x) Fill(i + 1, rockKinds[i], queue, ref write);
                if (z > 0) Fill(i - map.Size.x, rockKinds[i], queue, ref write);
                if (z + 1 < map.Size.z) Fill(i + map.Size.x, rockKinds[i], queue, ref write);
            }
            ushort fallback = CodeFor(DefaultRock());
            for (int i = 0; i < rockKinds.Length; i++)
                if (footprint[i] && rockKinds[i] == 0) rockKinds[i] = fallback;
        }

        private void Fill(int i, ushort code, int[] queue, ref int write)
        {
            if (!footprint[i] || rockKinds[i] != 0) return;
            rockKinds[i] = code;
            queue[write++] = i;
        }

        public void NotifyCell(IntVec3 c)
        {
            if (c.InBounds(map)) pending.Add(c.z * map.Size.x + c.x);
        }

        public void NotifyThing(Thing t)
        {
            if (t == null || t.def == null || t.def.category != ThingCategory.Building) return;
            foreach (var c in GenAdj.OccupiedRect(t)) NotifyCell(c);
        }

        public void InvalidateCaps() => capsDirty = true;

        public void UpdateField()
        {
            Capture();
            changed.Clear();
            ShapeChanged = false;
            if (!initialized) return;
            foreach (int i in pending)
            {
                var c = new IntVec3(i % map.Size.x, 0, i / map.Size.x);
                var ed = c.GetEdifice(map);
                bool rock = IsRock(ed);
                bool roof = NaturalRoof(map.roofGrid.RoofAt(c));
                if ((rock || roof) && !footprint[i])
                {
                    footprint[i] = true;
                    shapeDirty = true;
                }
                if (rock) rockKinds[i] = CodeFor(ed.def);
                if (footprint[i] && rockKinds[i] == 0) rockKinds[i] = CodeFor(DefaultRock());
                Cells[i] = rock ? (byte)1 : roof ? (byte)2 : (byte)0;
                changed.Add(i);
            }
            pending.Clear();
            if (shapeDirty)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                if (Shape == null) Shape = new MountainShape3D(map.Size.x, map.Size.z, seed);
                Shape.Build(footprint);
                LastBuildMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency);
                shapeDirty = false;
                ShapeChanged = true;
            }
            RefreshCaps();
        }

        private void RefreshCaps()
        {
            var stack = LevelStack3D.StackMapsAt(map);
            int level = stack.IndexOf(map);
            int signature = level;
            for (int j = level + 1; j < stack.Count; j++)
                if (stack[j] != null && !stack[j].Disposed)
                    signature = unchecked(signature * 397 ^ stack[j].uniqueID);
            if (!capsDirty && signature == upperSignature) return;
            upperSignature = signature;
            capsDirty = false;
            above.Clear();
            aboveY.Clear();
            for (int j = level + 1; j < stack.Count; j++)
            {
                var upper = stack[j];
                if (upper == null || upper.Disposed) continue;
                above.Add(upper);
                aboveY.Add((j - level) * Fps3DState.StoryHeight);
            }
            for (int i = 0; i < Cells.Length; i++)
            {
                var c = new IntVec3(i % map.Size.x, 0, i / map.Size.x);
                float cap = float.PositiveInfinity;
                for (int j = 0; j < above.Count; j++)
                {
                    var upper = above[j];
                    if (!c.InBounds(upper) || IsRock(c.GetEdifice(upper))) continue;
                    var terrain = upper.terrainGrid.TerrainAt(c);
                    if (terrain == null || terrain.dontRender || NaturalRoof(upper.roofGrid.RoofAt(c))) continue;
                    cap = aboveY[j] - 0.01f;
                    break;
                }
                if (Ceilings[i] == cap) continue;
                Ceilings[i] = cap;
                changed.Add(i);
            }
        }

        public ThingDef RockDefAt(int x, int z)
        {
            if (!Ready || x < 0 || z < 0 || x >= map.Size.x || z >= map.Size.z) return null;
            int code = rockKinds[z * map.Size.x + x];
            return code > 0 && code <= rockDefs.Count ? rockDefs[code - 1] : null;
        }

        public bool TryInterval(float x, float z, out float bottom, out float top)
        {
            bottom = top = 0f;
            int cx = Mathf.FloorToInt(x), cz = Mathf.FloorToInt(z);
            if (!Ready || cx < 0 || cz < 0 || cx >= map.Size.x || cz >= map.Size.z) return false;
            int i = cz * map.Size.x + cx;
            if (Cells[i] == 0) return false;
            bottom = Cells[i] == 1 ? 0f : Metrics3D.MountainCaveCeiling;
            top = Mathf.Min(Shape.HeightAt(x, z), Ceilings[i]);
            return top > bottom + 0.00001f;
        }

        public bool Contains(float x, float y, float z)
        {
            return TryInterval(x, z, out float lo, out float hi) && y >= lo && y <= hi;
        }

        public float TopAt(float x, float z)
        {
            return TryInterval(x, z, out _, out float hi) ? hi : 0f;
        }
    }
}
