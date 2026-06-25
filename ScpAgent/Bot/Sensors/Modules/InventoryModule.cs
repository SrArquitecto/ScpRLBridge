using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using System;
using Exiled.API.Enums;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class InventoryModule : ISensorInventoryModule
    {
        private Player _player;
        private readonly InventoryItemData[] _inventoryPool = new InventoryItemData[8];
        public InventoryModule()
        {
            for (int i = 0; i < _inventoryPool.Length;  i++) _inventoryPool[i]  = new InventoryItemData();
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {
            
        }
        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            _CargarInventario(obs);
        }
        private void _CargarInventario(AgentObservation obs)
        {
            obs.Inventory.Clear();
            if (_player == null || !_player.IsAlive) return;

            var itemEquipado = _player.CurrentItem;

            int countKeycards = 0;
            int countFirearms = 0;
            int countMedicals = 0;
            int countArmor = 0;
            int countGrenades = 0;
            int countScpItems = 0;
            int countOthers   = 0;

            //string categoriaItemEquipado = ModuleUtils.CategorizarItem(itemEquipado.Type);
            int slotIndex = 0;
            foreach (var item in _player.Items)
            {
                if (slotIndex >= 8) break;
                if (item == null) continue;

                string categoria = ModuleUtils.CategorizarItem(item.Type) ?? "Other";

                switch (categoria)
                {
                    case "Keycard":  countKeycards++; break;
                    case "Firearm":  countFirearms++; break;
                    case "Medical":  countMedicals++; break;
                    case "Armor":    countArmor++;    break;
                    case "Tactical": countGrenades++; break;
                    case "SCP":      countScpItems++; break; // O como se llame en tu función
                    default:         countOthers++;   break;
                }


                var inv = _inventoryPool[slotIndex];
                inv.Type       = item.Type.ToString();
                inv.Category   = categoria;
                inv.Tier       = ModuleUtils.GetKeycardTier(item.Type); //Generalizar para todos los items...
                inv.IsEquipped = itemEquipado != null && item.Serial == itemEquipado.Serial;
                inv.Ammo       = 0;

                // Balas en cargador solo del arma equipada
                try
                {
                    if (inv.IsEquipped)
                    {
                        var firearmsItem = item as Exiled.API.Features.Items.Firearm;
                        if (firearmsItem != null)
                            inv.Ammo = firearmsItem.MagazineAmmo;
                    }
                }
                catch { }

                obs.Inventory.Add(inv);
                slotIndex++;
            }

            obs.InventorySlots = 8 - slotIndex;

            // Munición en reserva — sistema separado del inventario
            try
            {
                obs.CountKeycards = (float)countKeycards/3f;
                obs.CountFirearms = (float)countFirearms/3f;
                obs.CountMedicals = (float)countMedicals/3f;
                obs.CountArmor    = (float)countArmor/1f;
                obs.CountGrenades = (float)countGrenades/3f;
                obs.CountScpItems = (float)countScpItems/3f;
                obs.CountOthers   = (float)countOthers/3f;
                obs.Ammo9x19    = _player.GetAmmo(AmmoType.Nato9);
                obs.Ammo12gauge = _player.GetAmmo(AmmoType.Ammo12Gauge);
                obs.Ammo556x45  = _player.GetAmmo(AmmoType.Nato556);
                obs.Ammo762x39  = _player.GetAmmo(AmmoType.Nato762);
                obs.Ammo44cal   = _player.GetAmmo(AmmoType.Ammo44Cal);
            }
            catch { }
        }

    }
}