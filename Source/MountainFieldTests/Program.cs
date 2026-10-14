using System;
using System.Collections.Generic;
using System.Linq;
using RimFirstPerson3D;
using Verse;

namespace MountainFieldTests
{
    internal static class Program
    {
        private static int passed, failed;
        private static readonly RoofDef thin = new RoofDef { isNatural = true };
        private static readonly RoofDef thick = new RoofDef { isNatural = true, isThickRoof = true };
        private static readonly ThingDef wall = new ThingDef { category = ThingCategory.Building, Fillage = FillCategory.Full };
        private static readonly ThingDef rock = new ThingDef
        {
            category = ThingCategory.Building,
            Fillage = FillCategory.Full,
            building = new BuildingProperties { isNaturalRock = true }
        };

        private static void Main()
        {
            DefDatabase<ThingDef>.AllDefsListForReading.Add(rock);
            TestProperNaturalRoof(thin);
            TestProperNaturalRoof(thick);
            TestUnavailableRoomRecovers(false);
            TestUnavailableRoomRecovers(true);
            TestRoomRebuildNotification();
            TestNotificationDuringRoomQuery();
            TestStableNullRoomDoesNotPoll();
            TestDisabledUpdaterDefersClassification();
            TestEnabledUpdaterRebuildsBeforeClassification();
            TestMapEventIsolation();
            TestRoofRemovalAndArtificialRoof();
            TestRockConnectionAndUnroofedOpening();
            Console.WriteLine($"Test Results: Passed={passed}, Failed={failed}");
            if (failed != 0) Environment.Exit(1);
        }

        private static void Assert(bool condition, string name)
        {
            Console.WriteLine((condition ? "[PASS] " : "[FAIL] ") + name);
            if (condition) passed++; else failed++;
        }

        private static MountainField3D Create(Map map)
        {
            var field = new MountainField3D(map);
            field.FinalizeInit();
            field.UpdateField();
            return field;
        }

        private static double CeilingArea(MountainField3D field, int x, int z)
        {
            var triangles = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(field.Shape, x, z, field.Cells, field.Ceilings, triangles);
            double area = 0;
            foreach (var triangle in triangles)
            {
                if (triangle.na.y > -0.9f) continue;
                area += Math.Abs((triangle.b.z - triangle.a.z) * (triangle.c.x - triangle.a.x)
                    - (triangle.b.x - triangle.a.x) * (triangle.c.z - triangle.a.z)) * 0.5;
            }
            return area;
        }

        private static void TestProperNaturalRoof(RoofDef roof)
        {
            var map = new Map(7, 7);
            int index = 3 * 7 + 3;
            map.roofGrid.Data[index] = roof;
            map.Rooms[index] = new Room { ProperRoom = true };
            var field = Create(map);
            Assert(field.Cells[index] == 2 && Math.Abs(CeilingArea(field, 3, 3) - 1) < 1e-6,
                (roof.isThickRoof ? "Thick" : "Thin") + " proper-room roof has a complete ceiling");
        }

        private static void TestUnavailableRoomRecovers(bool enclosedWalls)
        {
            var map = new Map(7, 7);
            int index = 3 * 7 + 3;
            map.roofGrid.Data[index] = thin;
            if (enclosedWalls)
                foreach (int offset in new[] { -1, 1, -7, 7 })
                    map.edificeGrid.Data[index + offset] = new Thing { def = wall };
            var field = Create(map);
            map.Rooms[index] = new Room { ProperRoom = true };
            map.events.Notify_RegionsRoomsChanged();
            field.UpdateField();
            Assert(field.Cells[index] == 2 && Math.Abs(CeilingArea(field, 3, 3) - 1) < 1e-6,
                "Room-rebuild event restores initially unavailable room, walls=" + enclosedWalls);
        }

        private static void TestRoomRebuildNotification()
        {
            var map = new Map(7, 7);
            int index = 3 * 7 + 3;
            map.roofGrid.Data[index] = thin;
            var room = new Room();
            map.Rooms[index] = room;
            var field = Create(map);
            room.ProperRoom = true;
            map.events.Notify_RegionsRoomsChanged();
            field.UpdateField();
            Assert(field.Cells[index] == 2 && Math.Abs(CeilingArea(field, 3, 3) - 1) < 1e-6,
                "Room rebuild restores roof removed by outdoor flood");
            room.ProperRoom = false;
            map.events.Notify_RegionsRoomsChanged();
            field.UpdateField();
            Assert(field.Cells[index] == 0 && CeilingArea(field, 3, 3) == 0,
                "Room opened to outdoors removes stale protected roof");
        }

        private static void TestNotificationDuringRoomQuery()
        {
            var map = new Map(7, 7);
            int index = 3 * 7 + 3;
            var room = new Room();
            map.roofGrid.Data[index] = thin;
            map.Rooms[index] = room;
            var field = Create(map);
            map.BeforeRoomQuery = () => { map.BeforeRoomQuery = null; map.events.Notify_RegionsRoomsChanged(); };
            field.NotifyCell(new IntVec3(3, 0, 3));
            field.UpdateField();
            room.ProperRoom = true;
            field.UpdateField();
            Assert(field.Cells[index] == 2 && Math.Abs(CeilingArea(field, 3, 3) - 1) < 1e-6,
                "Invalidation arriving inside room query is not consumed");
        }

        private static void TestStableNullRoomDoesNotPoll()
        {
            var map = new Map(7, 7);
            map.roofGrid.Data[3 * 7 + 3] = thin;
            int queries = 0;
            map.BeforeRoomQuery = () => queries++;
            var field = Create(map);
            int firstQueries = queries;
            for (int i = 0; i < 20; i++) field.UpdateField();
            Assert(firstQueries > 0 && queries == firstQueries,
                "Stable null room does not cause per-frame roof scans");
        }

        private static void TestDisabledUpdaterDefersClassification()
        {
            var map = new Map(7, 7);
            int index = 3 * 7 + 3;
            map.roofGrid.Data[index] = thin;
            map.Rooms[index] = new Room { ProperRoom = true };
            var field = Create(map);
            map.Rooms[index] = null;
            map.regionAndRoomUpdater.Enabled = false;
            map.regionAndRoomUpdater.AnythingToRebuild = true;
            field.NotifyCell(new IntVec3(3, 0, 3));
            field.UpdateField();
            Assert(field.Cells[index] == 2 && Math.Abs(CeilingArea(field, 3, 3) - 1) < 1e-6,
                "Disabled dirty updater retains the last complete roof geometry");
            map.regionAndRoomUpdater.Rebuild = () =>
            {
                map.Rooms[index] = new Room { ProperRoom = false };
                map.events.Notify_RegionsRoomsChanged();
            };
            map.regionAndRoomUpdater.Enabled = true;
            field.UpdateField();
            Assert(field.Cells[index] == 0, "Deferred classification runs when the updater becomes ready");
            Assert(field.Changed.Contains(index), "Deferred pending cell changes survive until classification is ready");
        }

        private static void TestEnabledUpdaterRebuildsBeforeClassification()
        {
            var map = new Map(7, 7);
            int index = 3 * 7 + 3;
            map.roofGrid.Data[index] = thin;
            map.regionAndRoomUpdater.AnythingToRebuild = true;
            map.regionAndRoomUpdater.Rebuild = () =>
            {
                map.Rooms[index] = new Room { ProperRoom = true };
                map.events.Notify_RegionsRoomsChanged();
            };
            var field = Create(map);
            Assert(field.Cells[index] == 2 && Math.Abs(CeilingArea(field, 3, 3) - 1) < 1e-6,
                "Pending region rebuild completes before roof protection is sampled");
        }

        private static void TestMapEventIsolation()
        {
            var first = new Map(7, 7);
            var second = new Map(7, 7);
            int index = 3 * 7 + 3;
            var room = new Room();
            second.Rooms[index] = room;
            second.roofGrid.Data[index] = thin;
            var firstField = Create(first);
            var secondField = Create(second);
            firstField.FinalizeInit();
            room.ProperRoom = true;
            first.events.Notify_RegionsRoomsChanged();
            secondField.UpdateField();
            Assert(secondField.Cells[index] == 0, "Room events do not invalidate other maps");
            second.events.Notify_RegionsRoomsChanged();
            secondField.UpdateField();
            Assert(secondField.Cells[index] == 2, "Room events invalidate their own map component");
        }

        private static void TestRoofRemovalAndArtificialRoof()
        {
            var map = new Map(7, 7);
            int index = 3 * 7 + 3;
            map.Rooms[index] = new Room { ProperRoom = true };
            map.roofGrid.Data[index] = thin;
            var field = Create(map);
            map.roofGrid.Data[index] = null;
            field.NotifyCell(new IntVec3(3, 0, 3));
            field.UpdateField();
            Assert(field.Cells[index] == 0 && CeilingArea(field, 3, 3) == 0,
                "Removing natural roof keeps the original opening");
            map.roofGrid.Data[index] = new RoofDef();
            field.NotifyCell(new IntVec3(3, 0, 3));
            field.UpdateField();
            Assert(field.Cells[index] == 0, "Artificial roof does not enter the mountain layer");
        }

        private static void TestRockConnectionAndUnroofedOpening()
        {
            var map = new Map(9, 9);
            var room = new Room { ProperRoom = true };
            for (int z = 2; z <= 6; z++)
                for (int x = 2; x <= 6; x++)
                {
                    int index = z * 9 + x;
                    map.Rooms[index] = room;
                    if (x == 6) map.edificeGrid.Data[index] = new Thing { def = rock };
                    else if (x != 4 || z != 4) map.roofGrid.Data[index] = thin;
                }
            var field = Create(map);
            bool roofsComplete = true;
            for (int z = 2; z <= 6; z++)
                for (int x = 2; x < 6; x++)
                    if (x != 4 || z != 4)
                        roofsComplete &= Math.Abs(CeilingArea(field, x, z) - 1) < 1e-6;
            Assert(roofsComplete, "All protected roof cells remain complete next to rock and an opening");
            Assert(field.Cells[4 * 9 + 4] == 0 && CeilingArea(field, 4, 4) == 0,
                "Unroofed room cell is not filled by neighboring constraints");
            Assert(field.Cells[4 * 9 + 6] == 1 && CeilingArea(field, 6, 4) == 0,
                "Roof joins solid rock without a second ceiling inside it");
        }
    }
}
