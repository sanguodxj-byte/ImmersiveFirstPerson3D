// AI生成
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimFirstPerson3D
{
    // 第一人称背包的"独立组件"：唯一负责背包物品的枚举、取用、增删与动作执行。
    //
    // 存放位置的取舍：物品真实存在附身者的原版背包（pawn.inventory.innerContainer）里，
    // 而不是另起一个模组私有容器。这样拾取/装备/负重/搬运/存档全部与原版一致，
    // 不会出现"背包里有东西但小人背不动、存档丢失、工作台拿不到料"的割裂。
    // 本组件的职责是把它包成第一人称下唯一、可操作的入口（UI 只跟它打交道）。
    //
    // 可用性：仅当处于第一人称且已附身时可用（ViewMode.FirstPerson）。
    public static class Backpack3D
    {
        // 快捷栏槽位数（对应数字键 1..N）。
        public const int HotbarSlots = 6;

        public static Pawn Owner => CameraRig3D.Possessed;

        public static bool Available =>
            Fps3DState.Active && Fps3DState.Mode == ViewMode.FirstPerson && Owner != null;

        public static Thing PrimaryWeapon =>
            (Owner != null && Owner.equipment != null) ? Owner.equipment.Primary : null;

        // 当前背包物品快照（跳过已销毁项；按标签稳定排序，供 UI 与快捷栏共用同一顺序）。
        public static List<Thing> Items()
        {
            var list = new List<Thing>();
            var pawn = Owner;
            if (pawn == null || pawn.inventory == null || pawn.inventory.innerContainer == null) return list;
            try
            {
                foreach (var t in pawn.inventory.innerContainer)
                    if (t != null && !t.Destroyed) list.Add(t);
            }
            catch { }
            list.Sort(CompareItems);
            return list;
        }

        private static int CompareItems(Thing a, Thing b)
        {
            if (a == null || b == null || a.def == null || b.def == null) return 0;
            int c = string.CompareOrdinal(a.def.label ?? a.def.defName ?? "", b.def.label ?? b.def.defName ?? "");
            if (c != 0) return c;
            return a.thingIDNumber.CompareTo(b.thingIDNumber);
        }

        // 数字键 1..N：对快照第 slot 件物品执行主要动作。
        public static bool UseSlot(int slot)
        {
            if (slot < 1 || slot > HotbarSlots) return false;
            var items = Items();
            if (slot > items.Count) return false;
            return PrimaryAction(items[slot - 1]);
        }

        // 主要动作：武器->装备；衣物->穿戴；可食用/可吸入->使用；其余不动作。
        public static bool PrimaryAction(Thing t)
        {
            var pawn = Owner;
            if (pawn == null || t == null || t.Destroyed || t.def == null) return false;
            try
            {
                if (t.def.IsWeapon) return Equip(t);
                if (t.def.IsApparel) return Wear(t);
                if (t.def.ingestible != null) return Use(t);
            }
            catch { }
            return false;
        }

        // 装备（背包内武器直接装备，与原版浮动菜单的 Equip 同一作业）。
        public static bool Equip(Thing t)
        {
            var pawn = Owner;
            if (pawn == null || t == null || pawn.jobs == null) return false;
            try
            {
                var job = JobMaker.MakeJob(JobDefOf.Equip, t);
                job.count = 1;
                return pawn.jobs.TryTakeOrderedJob(job);
            }
            catch { return false; }
        }

        // 穿戴（衣物）。
        public static bool Wear(Thing t)
        {
            var pawn = Owner;
            if (pawn == null || t == null || pawn.jobs == null) return false;
            try
            {
                var job = JobMaker.MakeJob(JobDefOf.Wear, t);
                return pawn.jobs.TryTakeOrderedJob(job);
            }
            catch { return false; }
        }

        // 使用（食物/药物：原版 Ingest 作业）。
        public static bool Use(Thing t)
        {
            var pawn = Owner;
            if (pawn == null || t == null || pawn.jobs == null) return false;
            try
            {
                var job = JobMaker.MakeJob(JobDefOf.Ingest, t);
                job.count = 1;
                return pawn.jobs.TryTakeOrderedJob(job);
            }
            catch { return false; }
        }

        // 丢弃：从背包把 count 件（默认整叠）放到附身者脚下。
        public static bool Drop(Thing t, int count = -1)
        {
            var pawn = Owner;
            if (pawn == null || t == null || t.Destroyed) return false;
            if (pawn.inventory == null || pawn.inventory.innerContainer == null) return false;
            try
            {
                if (!pawn.inventory.innerContainer.Contains(t)) return false;
                int n = count <= 0 ? t.stackCount : count;
                var map = pawn.MapHeld;
                if (map == null) return false;
                Thing dropped;
                if (pawn.inventory.innerContainer.TryDrop(t, pawn.PositionHeld, map,
                        ThingPlaceMode.Near, n, out dropped) && dropped != null)
                {
                    dropped.SetForbidden(false, false);
                    return true;
                }
            }
            catch { }
            return false;
        }

        // 该物品在第一人称下的主要动作按钮文案（取原版翻译键，缺失时回退中文）。
        public static string PrimaryActionLabel(Thing t)
        {
            if (t == null || t.def == null) return "";
            try
            {
                if (t.def.IsWeapon) return T("Equip", "装备");
                if (t.def.IsApparel) return T("Wear", "穿戴");
                if (t.def.ingestible != null) return T("Consume", "使用");
            }
            catch { }
            return "";
        }

        // 原版翻译键优先；键缺失时 string.Translate() 会原样返回键名，此时用中文兜底。
        private static string T(string key, string fallback)
        {
            string s = key.Translate();
            return string.IsNullOrEmpty(s) || s == key ? fallback : s;
        }
    }
}
