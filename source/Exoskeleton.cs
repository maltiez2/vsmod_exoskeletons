﻿using CombatOverhaul;
using CombatOverhaul.Armor;
using CombatOverhaul.DamageSystems;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Exoskeleton;

public class ExoskeletonStats
{
    public bool NeedsFuel { get; set; } = true;
    public float FuelCapacityHours { get; set; } = 24f;
    public float FuelEfficiency { get; set; } = 1f;
    public string FuelAttribute { get; set; } = "nightVisionFuelHours";

    public string[] Layers { get; set; } = Array.Empty<string>();
    public string[] Zones { get; set; } = Array.Empty<string>();
    public Dictionary<string, float> Resists { get; set; } = new();
    public Dictionary<string, float> FlatReduction { get; set; } = new();
    public Dictionary<string, float> StatsWhenTurnedOn { get; set; } = new();
    public Dictionary<string, float> StatsWhenTurnedOff { get; set; } = new();
}

public class Exoskeleton : ItemWearable, IFueledItem, IAffectsPlayerStats, IArmor
{
    public ArmorType ArmorType { get; private set; }
    public DamageResistData Resists { get; private set; } = new();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        _stats = Attributes.AsObject<ExoskeletonStats>();

        if (!_stats.Layers.Any() || !_stats.Zones.Any())
        {
            return;
        }

        ArmorType = new(_stats.Layers.Select(Enum.Parse<ArmorLayers>).Aggregate((first, second) => first | second), _stats.Zones.Select(Enum.Parse<DamageZone>).Aggregate((first, second) => first | second));
        Resists = new(
            _stats.Resists.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value),
            _stats.FlatReduction.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value));
    }

    public void AddFuelHours(ItemSlot slot, double hours)
    {
        if (slot?.Itemstack?.Attributes == null) return;

        slot.Itemstack.Attributes.SetDouble("fuelHours", Math.Max(0.0, hours + GetFuelHours(slot)));
        slot.OnItemSlotModified(sinkStack: null);
    }
    public double GetFuelHours(ItemSlot slot)
    {
        if (slot?.Itemstack?.Attributes == null) return 0;

        return Math.Max(0.0, slot.Itemstack.Attributes.GetDecimal(_fuelAttribute));
    }
    public static void SetFuelHours(ItemSlot slot, double fuelHours)
    {
        if (slot?.Itemstack?.Attributes == null) return;

        slot.Itemstack.Attributes.SetDouble("fuelHours", fuelHours);
        slot.MarkDirty();
    }
    public float GetStackFuel(ItemStack stack)
    {
        return (stack.ItemAttributes?[_stats.FuelAttribute].AsFloat() ?? 0f) * _stats.FuelEfficiency;
    }

    public Dictionary<string, float> PlayerStats(ItemSlot slot, EntityPlayer player)
    {
        double fuelLeft = GetFuelHours(slot);

        return fuelLeft > 0 ? _stats.StatsWhenTurnedOn : _stats.StatsWhenTurnedOff;
    }

    public override int GetMergableQuantity(ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
    {
        if (priority == EnumMergePriority.DirectMerge)
        {
            if (GetStackFuel(sourceStack) == 0f)
            {
                return base.GetMergableQuantity(sinkStack, sourceStack, priority);
            }

            return 1;
        }

        return base.GetMergableQuantity(sinkStack, sourceStack, priority);
    }
    public override void TryMergeStacks(ItemStackMergeOperation op)
    {
        if (op.CurrentPriority == EnumMergePriority.DirectMerge)
        {
            float stackFuel = GetStackFuel(op.SourceSlot.Itemstack);
            double fuelHours = GetFuelHours(op.SinkSlot);
            if (stackFuel > 0f && fuelHours + (double)(stackFuel / 2f) < (double)_stats.FuelCapacityHours)
            {
                SetFuelHours(op.SinkSlot, (double)stackFuel + fuelHours);
                op.MovedQuantity = 1;
                op.SourceSlot.TakeOut(1);
                op.SinkSlot.MarkDirty();
            }
            else if (api.Side == EnumAppSide.Client)
            {
                (api as ICoreClientAPI)?.TriggerIngameError(this, "maskfull", Lang.Get("ingameerror-mask-full")); // @TODO change error message
            }
        }
    }
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        
        double fuelHours = GetFuelHours(inSlot);
        dsc.AppendLine(Lang.Get("Has fuel for {0:0.#} hours", fuelHours));
        if (fuelHours <= 0.0)
        {
            dsc.AppendLine(Lang.Get("Add temporal gear to refuel"));
        }

        dsc.AppendLine();
        dsc.AppendLine(Lang.Get("combatoverhaul:armor-layers-info", ArmorType.LayersToTranslatedString()));
        dsc.AppendLine(Lang.Get("combatoverhaul:armor-zones-info", ArmorType.ZonesToTranslatedString()));
        if (Resists.Resists.Values.Any(value => value != 0))
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:armor-fraction-protection"));
            foreach ((EnumDamageType type, float level) in Resists.Resists.Where(entry => entry.Value > 0))
            {
                string damageType = Lang.Get($"combatoverhaul:damage-type-{type}");
                dsc.AppendLine($"  {damageType}: {level}");
            }
        }

        if (Resists.FlatDamageReduction.Values.Any(value => value != 0))
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:armor-flat-protection"));
            foreach ((EnumDamageType type, float level) in Resists.FlatDamageReduction.Where(entry => entry.Value > 0))
            {
                string damageType = Lang.Get($"combatoverhaul:damage-type-{type}");
                dsc.AppendLine($"  {damageType}: {level}");
            }
        }

        if (_stats.StatsWhenTurnedOn.Values.Any(value => value != 0))
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:stat-stats"));
            foreach ((string stat, float value) in _stats.StatsWhenTurnedOn)
            {
                if (value != 0f) dsc.AppendLine($"  {Lang.Get($"combatoverhaul:stat-{stat}")}: {value * 100:F1}%");
            }
        }

        dsc.AppendLine();
    }
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        if (byEntity.Controls.ShiftKey)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
            return;
        }

        if (slot.Itemstack.Item == null) return;

        IPlayer? player = (byEntity as EntityPlayer)?.Player;
        if (player == null) return;

        ArmorInventory? inventory = GetGearInventory(byEntity) as ArmorInventory;
        if (inventory == null) return;

        string code = slot.Itemstack.Item.Code;

        try
        {
            IEnumerable<int> slots = inventory.GetSlotBlockingSlotsIndices(ArmorType);

            foreach (int index in slots)
            {
                ItemStack stack = inventory[index].TakeOutWhole();
                if (!player.InventoryManager.TryGiveItemstack(stack))
                {
                    byEntity.Api.World.SpawnItemEntity(stack, byEntity.ServerPos.AsBlockPos);
                }
                inventory[index].MarkDirty();
            }

            int slotIndex = inventory.GetFittingSlotIndex(ArmorType);
            inventory[slotIndex].TryFlipWith(slot);

            inventory[slotIndex].MarkDirty();
            slot.MarkDirty();

            handHandling = EnumHandHandling.PreventDefault;
        }
        catch (Exception exception)
        {
            api.Logger.Error($"[Exoskeleton] Error on equipping '{code}' that occupies {ArmorType}:\n{exception}");
        }
    }

    private const string _fuelAttribute = "fuelHours";
    private ExoskeletonStats _stats = new();

    private static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>().Inventory;
    }
}
